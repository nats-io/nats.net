using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace NATS.Client.Core.Internal;

internal class TlsCerts
{
    private static readonly Regex PemRegex = new(@"^\s*-{5}BEGIN\s+([^-]+)-{5}[A-Za-z0-9=+/\s\r\n]+-{5}END\s+\1-{5}\s*$", RegexOptions.Singleline | RegexOptions.Compiled);

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
            CaCerts.ImportFromPem(ReadPemStringOrFile(tlsOpts.CaFile));
        }

        if (tlsOpts.CertFile != default && tlsOpts.KeyFile != default)
        {
            var clientCert = X509Certificate2.CreateFromPem(ReadPemStringOrFile(tlsOpts.CertFile), ReadPemStringOrFile(tlsOpts.KeyFile));

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

    private static string ReadPemStringOrFile(string input) => (PemRegex.IsMatch(input) ? input : File.ReadAllText(input)).Trim();
}
