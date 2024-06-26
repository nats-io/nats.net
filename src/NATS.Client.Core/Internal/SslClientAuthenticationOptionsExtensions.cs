using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using X509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;

#if NETSTANDARD
using System.Text;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.IO.Pem;
using Org.BouncyCastle.X509;
#endif

namespace NATS.Client.Core.Internal;

internal static class SslClientAuthenticationOptionsExtensions
{
    public static SslClientAuthenticationOptions LoadClientCertFromPem(this SslClientAuthenticationOptions options, string certPem, string keyPem, bool offline = false, SslCertificateTrust? trust = null)
    {
#if NETSTANDARD
        var leafCert = X509Certificate2Helpers.CreateFromPem(certPem, keyPem);
#else
        var leafCert = X509Certificate2.CreateFromPem(certPem, keyPem);
#endif
        var intermediateCerts = new X509Certificate2Collection();
        intermediateCerts.ImportFromPem(certPem);
        if (intermediateCerts.Count > 0)
        {
            intermediateCerts.RemoveAt(0);
        }

#if !NET8_0_OR_GREATER
        if (intermediateCerts.Count > 0)
        {
            throw new NotSupportedException("Client Certificates with intermediates are only supported in net8.0 and higher");
        }
#endif

        return options.LoadClientCertFromX509(leafCert, intermediateCerts, offline, trust);
    }

    public static SslClientAuthenticationOptions LoadCaCertsFromPem(this SslClientAuthenticationOptions options, string caPem)
    {
        var caCerts = new X509Certificate2Collection();
        caCerts.ImportFromPem(caPem);

        return options.LoadCaCertsFromX509(caCerts);
    }

    public static SslClientAuthenticationOptions LoadClientCertFromX509(this SslClientAuthenticationOptions options, X509Certificate2 leafCert, X509Certificate2Collection? intermediateCerts = null, bool offline = false, SslCertificateTrust? trust = null)
    {
        // On Windows, ephemeral keys/certificates do not work with schannel. e.g. unless stored in certificate store.
        // https://github.com/dotnet/runtime/issues/66283#issuecomment-1061014225
        // https://github.com/dotnet/runtime/blob/380a4723ea98067c28d54f30e1a652483a6a257a/src/libraries/System.Net.Security/tests/FunctionalTests/TestHelper.cs#L192-L197
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ephemeral = leafCert;
            leafCert = new X509Certificate2(leafCert.Export(X509ContentType.Pfx));
            ephemeral.Dispose();
        }

#if NET8_0_OR_GREATER
        options.ClientCertificateContext = SslStreamCertificateContext.Create(leafCert, intermediateCerts, offline, trust);
#else
        options.ClientCertificates = new X509Certificate2Collection(leafCert);
        options.LocalCertificateSelectionCallback = LcsCbClientCerts;
#endif

        return options;

#if !NET8_0_OR_GREATER
        static X509Certificate LcsCbClientCerts(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate? remoteCertificate,
            string[] acceptableIssuers) => localCertificates[0];
#endif
    }

    public static SslClientAuthenticationOptions LoadCaCertsFromX509(this SslClientAuthenticationOptions options, X509Certificate2Collection caCerts)
    {
        options.RemoteCertificateValidationCallback = RcsCbCaCertChain;
        return options;

        bool RcsCbCaCertChain(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // validate >=1 ca certs
            if (!caCerts.OfType<X509Certificate2>().Any())
            {
                return false;
            }

            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0
                && chain != default
                && certificate != default)
            {
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.ExtraStore.AddRange(caCerts);
                if (chain.Build((X509Certificate2)certificate))
                {
                    sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
                }
            }

            // validate >= 1 chain elements and that last chain element was one of the supplied CA certs
            if (chain == default
                || !chain.ChainElements.OfType<X509ChainElement>().Any()
                || !caCerts.OfType<X509Certificate2>().Any(c => c.RawData.SequenceEqual(chain.ChainElements.OfType<X509ChainElement>().Last().Certificate.RawData)))
            {
                sslPolicyErrors |= SslPolicyErrors.RemoteCertificateChainErrors;
            }

            return sslPolicyErrors == SslPolicyErrors.None;
        }
    }

    public static SslClientAuthenticationOptions InsecureSkipVerify(this SslClientAuthenticationOptions options)
    {
        options.RemoteCertificateValidationCallback = RcsCbInsecureSkipVerify;
        return options;

        static bool RcsCbInsecureSkipVerify(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors) => true;
    }
}

#if NETSTANDARD
internal static class X509Certificate2Helpers
{
    public static void ImportFromPem(this X509Certificate2Collection certs, string pem)
    {
        using var reader = new StringReader(pem);
        using var pemReader = new PemReader(reader);
        while (pemReader.ReadPemObject() is { } pemObject)
        {
            var cert = new X509Certificate2(pemObject.Content);
            certs.Add(cert);
        }
    }

    public static X509Certificate2 CreateFromPem(string certPem, string keyPem)
    {
        var certParser = new X509CertificateParser();
        var cert = certParser.ReadCertificate(new MemoryStream(Encoding.UTF8.GetBytes(certPem)));

        AsymmetricKeyParameter privateKey;
        using (var reader = new StringReader(keyPem))
        {
            var pemReader = new PemReader(reader);
            var pemObject = pemReader.ReadPemObject();
            var privateKeyInfo = PrivateKeyInfo.GetInstance(pemObject.Content);
            privateKey = PrivateKeyFactory.CreateKey(privateKeyInfo);
        }

        var store = new Pkcs12StoreBuilder().Build();
        const string name = "cert";
        var certificateEntry = new X509CertificateEntry(cert);
        store.SetCertificateEntry(name, certificateEntry);

        store.SetKeyEntry(name, new AsymmetricKeyEntry(privateKey), new[] { certificateEntry });
        using var stream = new MemoryStream();
        store.Save(stream, [], SecureRandom.GetInstance("SHA256PRNG"));
        var certWithKey = new X509Certificate2(
            stream.ToArray(),
            string.Empty,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        return certWithKey;
    }
}

internal class SslCertificateTrust;
#endif
