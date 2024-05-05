using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core.Internal;

internal static class SslClientAuthenticationOptionsExtensions
{
    public static SslClientAuthenticationOptions LoadClientCertFromPem(this SslClientAuthenticationOptions options, string certPem, string keyPem, bool offline = false, SslCertificateTrust? trust = null)
    {
        var leafCert = X509Certificate2.CreateFromPem(certPem, keyPem);
        var intermediateCerts = new X509Certificate2Collection();
        intermediateCerts.ImportFromPem(certPem);
        if (intermediateCerts.Count > 0)
        {
            intermediateCerts.RemoveAt(0);
        }

#if NET6_0
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

#if NET6_0
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
            if (!caCerts.Any())
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
                || !chain.ChainElements.Any()
                || !caCerts.Any(c => c.RawData.SequenceEqual(chain.ChainElements.Last().Certificate.RawData)))
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
