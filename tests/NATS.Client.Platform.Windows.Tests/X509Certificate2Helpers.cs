using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.IO.Pem;
using Org.BouncyCastle.X509;

namespace NATS.Client.Platform.Windows.Tests;

public static class X509Certificate2Helpers
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
