using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

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
        var clientCertsProvider = tlsOpts switch
        {
            { CertFile: not null, KeyFile: not null } => NatsTlsOpts.LoadClientCertFromPemFile(tlsOpts.CertFile, tlsOpts.KeyFile),
            { LoadClientCert: not null } => tlsOpts.LoadClientCert,
            _ => null,
        };

        var clientCerts = clientCertsProvider != null ? await clientCertsProvider().ConfigureAwait(false) : null;

        if (clientCerts != null)
        {
            // On Windows, ephemeral keys/certificates do not work with schannel. e.g. unless stored in certificate store.
            // https://github.com/dotnet/runtime/issues/66283#issuecomment-1061014225
            // https://github.com/dotnet/runtime/blob/380a4723ea98067c28d54f30e1a652483a6a257a/src/libraries/System.Net.Security/tests/FunctionalTests/TestHelper.cs#L192-L197
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var windowsCerts = new X509Certificate2Collection();
                foreach (var ephemeralCert in clientCerts)
                {
                    windowsCerts.Add(new X509Certificate2(ephemeralCert.Export(X509ContentType.Pfx)));
                    ephemeralCert.Dispose();
                }

                clientCerts = windowsCerts;
            }

            tlsCerts.ClientCerts = clientCerts;
        }

        return tlsCerts;
    }
}
