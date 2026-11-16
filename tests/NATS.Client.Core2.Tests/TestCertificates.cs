#if NET8_0_OR_GREATER
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core2.Tests;

/// <summary>
/// Throwaway PKI for TLS tests that need certificates the checked-in ones can't provide,
/// e.g. a server certificate with DNS names only (no IP SANs).
/// </summary>
internal sealed class TestCertificates : IDisposable
{
    private readonly string _dir;

    public TestCertificates()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nats-net-test-certs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Ca = CreateCa("NATS test CA");
        CaFile = Path.Combine(_dir, "ca.pem");
        File.WriteAllText(CaFile, Ca.ExportCertificatePem());
    }

    public X509Certificate2 Ca { get; }

    /// <summary>
    /// PEM file of <see cref="Ca"/>, for <see cref="NatsTlsOpts.CaFile"/>.
    /// </summary>
    public string CaFile { get; }

    /// <summary>
    /// A server certificate issued by <see cref="Ca"/>.
    /// </summary>
    public X509Certificate2 CreateServerCert(string[] dnsNames, IPAddress[]? ipAddresses = null) => CreateLeaf(Ca, dnsNames, ipAddresses ?? []);

    /// <summary>
    /// A server certificate issued by a CA the client does not trust.
    /// </summary>
    public X509Certificate2 CreateUntrustedServerCert(string[] dnsNames)
    {
        using var otherCa = CreateCa("Untrusted test CA");
        return CreateLeaf(otherCa, dnsNames, []);
    }

    public void Dispose()
    {
        Ca.Dispose();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // best effort
        }
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var now = DateTimeOffset.UtcNow;
        return Reload(request.CreateSelfSigned(now.AddDays(-1), now.AddDays(30)));
    }

    private static X509Certificate2 CreateLeaf(X509Certificate2 issuer, string[] dnsNames, IPAddress[] ipAddresses)
    {
        // ECDSA: RSA CopyWithPrivateKey fails on the .NET 8 runtime on recent macOS
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={(dnsNames.Length > 0 ? dnsNames[0] : "ip")}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var dnsName in dnsNames)
            san.AddDnsName(dnsName);
        foreach (var ipAddress in ipAddresses)
            san.AddIpAddress(ipAddress);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7f;
        var now = DateTimeOffset.UtcNow;
        using var cert = request.Create(issuer, now.AddDays(-1), new DateTimeOffset(issuer.NotAfter).AddDays(-1), serial);
        return Reload(cert.CopyWithPrivateKey(key));
    }

    // SslStream on macOS (and Windows) can't use an ephemeral private key for a server; a
    // round trip through PKCS#12 gives it one it can use.
    private static X509Certificate2 Reload(X509Certificate2 cert)
    {
        using (cert)
        {
            var pfx = cert.Export(X509ContentType.Pkcs12);
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pfx, null);
#else
            return new X509Certificate2(pfx);
#endif
        }
    }
}
#endif
