using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NATS.Client.Core;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit.Abstractions;
using X509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;

namespace NATS.Client.Platform.Windows.Tests;

public class TlsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Tls()
    {
        const string caCertPem = "resources/certs/ca-cert.pem";
        const string clientCertPem = "resources/certs/client-cert.pem";
        const string clientKeyPem = "resources/certs/client-key.pem";

        var tlsOpts = new NatsTlsOpts
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) => ValidateCert(errors, certificate, LoadCaCerts(caCertPem)),
            LoadClientCerts = () =>
            {
                var collection = LoadClientCerts(clientCertPem, clientKeyPem);
                return new ValueTask<X509Certificate2Collection>(collection);
            },
        };

        await using var server = await NatsServerProcess.StartAsync(config: "resources/configs/tls.conf");
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, TlsOpts = tlsOpts });

        await nats.PingAsync();

        await using var sub = await nats.SubscribeCoreAsync<int>("foo");
        for (var i = 0; i < 64; i++)
        {
            await nats.PublishAsync("foo", i);
            Assert.Equal(i, (await sub.Msgs.ReadAsync()).Data);
        }
    }

    private static bool ValidateCert(SslPolicyErrors errors, X509Certificate certificate, List<X509Certificate2> trustedCaCerts)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        var cert2 = new X509Certificate2(certificate);

        foreach (var caCert in trustedCaCerts)
        {
            var chain = new X509Chain();
            chain.ChainPolicy.ExtraStore.Add(caCert);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            if (!chain.Build(cert2))
                continue;
            var root = chain.ChainElements[^1].Certificate;
            if (root.Thumbprint == caCert.Thumbprint)
                return true;
        }

        return false;
    }

    private static List<X509Certificate2> LoadCaCerts(string pem)
    {
        List<X509Certificate2> caCertificates = [];

        using var reader = File.OpenText(pem);
        var pemReader = new PemReader(reader);
        while (pemReader.ReadObject() is { } obj)
        {
            if (obj is not Org.BouncyCastle.X509.X509Certificate bcCert)
                continue;
            var cert = new X509Certificate2(bcCert.GetEncoded());
            caCertificates.Add(cert);
        }

        return caCertificates;
    }

    private static X509Certificate2Collection LoadClientCerts(string certPemPath, string keyPemPath)
    {
        var collection = new X509Certificate2Collection();

        var certPem = File.ReadAllText(certPemPath);
        var certParser = new X509CertificateParser();
        var cert = certParser.ReadCertificate(new MemoryStream(Encoding.ASCII.GetBytes(certPem)));

        var keyPem = File.ReadAllText(keyPemPath);
        AsymmetricKeyParameter privateKey;
        using (var reader = new StringReader(keyPem))
        {
            var pemReader = new PemReader(reader);
            var pemObject = pemReader.ReadObject();
            if (pemObject is AsymmetricCipherKeyPair keyPair)
            {
                privateKey = keyPair.Private;
            }
            else
            {
                privateKey = (AsymmetricKeyParameter)pemObject;
            }
        }

        var x509Cert = new X509Certificate2(cert.GetEncoded());

        if (privateKey is RsaPrivateCrtKeyParameters rsaPrivateKey)
        {
            var rsaParams = DotNetUtilities.ToRSAParameters(rsaPrivateKey);
            var rsa = RSA.Create();
            rsa.ImportParameters(rsaParams);
            var certWithKey = x509Cert.CopyWithPrivateKey(rsa);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var ephemeral = certWithKey;
                certWithKey = new X509Certificate2(certWithKey.Export(X509ContentType.Pfx));
            }

            collection.Add(certWithKey);
        }
        else
        {
            throw new NotSupportedException("Unsupported key type");
        }

        return collection;
    }
}
