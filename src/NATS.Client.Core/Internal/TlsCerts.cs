using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core.Internal;

internal class TlsCerts
{
    public X509Certificate2Collection? CaCerts { get; private set; }

    public X509Certificate2Collection? ClientCerts { get; private set; }

    public static async ValueTask<TlsCerts> FromNatsTlsOptsAsync(NatsTlsOpts tlsOpts)
    {
        var tlsCerts = new TlsCerts();
        if (tlsOpts.Mode == TlsMode.Disable)
        {
            // no certs when disabled
            return tlsCerts;
        }

        // validation
        switch (tlsOpts)
        {
        case { CertFile: not null, KeyFile: null } or { KeyFile: not null, CertFile: null }:
            throw new ArgumentException("NatsTlsOpts.CertFile and NatsTlsOpts.KeyFile must both be set");
        case { CertFile: not null, KeyFile: not null, LoadClientCert: not null }:
            throw new ArgumentException("NatsTlsOpts.CertFile/KeyFile and NatsTlsOpts.LoadClientCert cannot both be set");
        case { CaFile: not null, LoadCaCerts: not null }:
            throw new ArgumentException("NatsTlsOpts.CaFile and NatsTlsOpts.LoadCaCerts cannot both be set");
        }

        // ca certificates
        if (tlsOpts.CaFile != default)
        {
            var caCerts = new X509Certificate2Collection();
            caCerts.ImportFromPemFile(tlsOpts.CaFile);
            tlsCerts.CaCerts = caCerts;
        }
        else if (tlsOpts.LoadCaCerts != default)
        {
            tlsCerts.CaCerts = await tlsOpts.LoadCaCerts().ConfigureAwait(false);
        }

        // client certificates
        var clientCert = tlsOpts switch
        {
            { CertFile: not null, KeyFile: not null } => X509Certificate2.CreateFromPemFile(tlsOpts.CertFile, tlsOpts.KeyFile),
            { LoadClientCert: not null } => await tlsOpts.LoadClientCert().ConfigureAwait(false),
            _ => null,
        };

        if (clientCert != null)
        {
            // On Windows, ephemeral keys/certificates do not work with schannel. e.g. unless stored in certificate store.
            // https://github.com/dotnet/runtime/issues/66283#issuecomment-1061014225
            // https://github.com/dotnet/runtime/blob/380a4723ea98067c28d54f30e1a652483a6a257a/src/libraries/System.Net.Security/tests/FunctionalTests/TestHelper.cs#L192-L197
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ephemeral = clientCert;
                clientCert = new X509Certificate2(clientCert.Export(X509ContentType.Pfx));
                ephemeral.Dispose();
            }

            tlsCerts.ClientCerts = new X509Certificate2Collection(clientCert);
        }

        return tlsCerts;
    }
}
