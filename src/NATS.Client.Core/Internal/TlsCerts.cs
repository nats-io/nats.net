using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core.Internal;

internal class TlsCerts
{
    public TlsCerts(NatsTlsOpts tlsOpts)
    {
        if (tlsOpts.Mode == TlsMode.Disable)
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
            var clientCert = X509Certificate2.CreateFromPemFile(tlsOpts.CertFile, tlsOpts.KeyFile);

            // On Windows, ephemeral keys/certificates do not work with schannel. e.g. unless stored in certificate store.
            // https://github.com/dotnet/runtime/issues/66283#issuecomment-1061014225
            // https://github.com/dotnet/runtime/blob/380a4723ea98067c28d54f30e1a652483a6a257a/src/libraries/System.Net.Security/tests/FunctionalTests/TestHelper.cs#L192-L197
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ephemeral = clientCert;
                clientCert = new X509Certificate2(clientCert.Export(X509ContentType.Pfx));
                ephemeral.Dispose();
            }

            ClientCerts = new X509Certificate2Collection(clientCert);
        }
    }

    public X509Certificate2Collection? CaCerts { get; }

    public X509Certificate2Collection? ClientCerts { get; }
}
