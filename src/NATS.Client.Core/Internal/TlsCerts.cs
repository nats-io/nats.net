using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core.Internal;

internal class TlsCerts
{
    public TlsCerts(NatsTlsOpts tlsOpts)
    {
        if (tlsOpts.Disabled)
        {
            return;
        }

        if ((tlsOpts.CertFile != default && tlsOpts.KeyFile == default) ||
            (tlsOpts.KeyFile != default && tlsOpts.CertFile == default))
        {
            throw new ArgumentException("NatsTlsOpts.CertFile and NatsTlsOpts.KeyFile must both be set");
        }

        if (tlsOpts.CaFile != default)
        {
            CaCerts = new X509Certificate2Collection();
            CaCerts.ImportFromPemFile(tlsOpts.CaFile);
        }

        if (tlsOpts.CertFile != default && tlsOpts.KeyFile != default)
        {
            ClientCerts = new X509Certificate2Collection(X509Certificate2.CreateFromPemFile(tlsOpts.CertFile, tlsOpts.KeyFile));
        }
    }

    public X509Certificate2Collection? CaCerts { get; }

    public X509Certificate2Collection? ClientCerts { get; }
}
