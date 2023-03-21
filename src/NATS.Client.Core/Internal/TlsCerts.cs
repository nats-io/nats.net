using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core.Internal;

internal class TlsCerts
{
    public TlsCerts(TlsOptions tlsOptions)
    {
        if (tlsOptions.Disabled)
        {
            return;
        }

        if ((tlsOptions.CertFile != default && tlsOptions.KeyFile == default) ||
            (tlsOptions.KeyFile != default && tlsOptions.CertFile == default))
        {
            throw new ArgumentException("TlsOptions.CertFile and TlsOptions.KeyFile must both be set");
        }

        if (tlsOptions.CaFile != default)
        {
            CaCerts = new X509Certificate2Collection();
            CaCerts.ImportFromPemFile(tlsOptions.CaFile);
        }

        if (tlsOptions.CertFile != default && tlsOptions.KeyFile != default)
        {
            ClientCerts = new X509Certificate2Collection(X509Certificate2.CreateFromPemFile(tlsOptions.CertFile, tlsOptions.KeyFile));
        }
    }

    public X509Certificate2Collection? CaCerts { get; }

    public X509Certificate2Collection? ClientCerts { get; }
}
