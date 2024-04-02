using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace NATS.Client.Core.Tests;

public class TlsCertsTest
{
    [Fact]
    public async Task Load_ca_cert()
    {
        const string caFile = "resources/certs/ca-cert.pem";

        // CA cert from file
        {
            var opts = new NatsTlsOpts { CaFile = caFile };
            var certs = await TlsCerts.FromNatsTlsOptsAsync(opts);

            Assert.NotNull(certs.CaCerts);
            Assert.Single(certs.CaCerts);
            foreach (var cert in certs.CaCerts)
            {
                cert.Subject.Should().Be("CN=ca");
            }
        }

        // CA cert from PEM string
        {
            var opts = new NatsTlsOpts { LoadCaCerts = NatsTlsOpts.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile)) };
            var certs = await TlsCerts.FromNatsTlsOptsAsync(opts);

            Assert.NotNull(certs.CaCerts);
            Assert.Single(certs.CaCerts);
            foreach (var cert in certs.CaCerts)
            {
                cert.Subject.Should().Be("CN=ca");
            }
        }
    }

    [Fact]
    public async Task Load_client_cert_and_key()
    {
        const string clientCertFile = "resources/certs/client-cert.pem";
        const string clientKeyFile = "resources/certs/client-key.pem";

        await ValidateAsync(new NatsTlsOpts
        {
            CertFile = clientCertFile,
            KeyFile = clientKeyFile,
        });
        await ValidateAsync(new NatsTlsOpts
        {
            LoadClientCert = NatsTlsOpts.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile)),
        });

        return;

        static async Task ValidateAsync(NatsTlsOpts opts)
        {
            var certs = await TlsCerts.FromNatsTlsOptsAsync(opts);

            Assert.NotNull(certs.ClientCerts);
            Assert.Single(certs.ClientCerts);
            foreach (var c in certs.ClientCerts)
            {
                c.Subject.Should().Be("CN=client");
                var encryptValue = c.GetRSAPublicKey()!.Encrypt(Encoding.UTF8.GetBytes("test123"), RSAEncryptionPadding.OaepSHA1);
                var decryptValue = c.GetRSAPrivateKey()!.Decrypt(encryptValue, RSAEncryptionPadding.OaepSHA1);
                Encoding.UTF8.GetString(decryptValue).Should().Be("test123");
            }
        }
    }

    [Fact]
    public async Task Load_client_cert_chain_and_key()
    {
        const string clientCertFile = "resources/certs/chainedclient-cert.pem";
        const string clientKeyFile = "resources/certs/chainedclient-key.pem";

        await ValidateAsync(new NatsTlsOpts
        {
            CertFile = clientCertFile,
            KeyFile = clientKeyFile,
        });
#if NET8_0_OR_GREATER
        await ValidateAsync(new NatsTlsOpts
        {
            LoadClientCertContext = NatsTlsOpts.LoadClientCertContextFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile)),
        });
#endif

        return;

        static async Task ValidateAsync(NatsTlsOpts opts)
        {
            var certs = await TlsCerts.FromNatsTlsOptsAsync(opts);
            Assert.NotNull(certs.ClientCerts);
            Assert.Single(certs.ClientCerts);

            var cc = new List<X509Certificate2> { certs.ClientCerts[0] };

#if NET8_0_OR_GREATER
            Assert.NotNull(certs.ClientCertContext);
            cc.Add(certs.ClientCertContext.TargetCertificate);
#endif

            foreach (var cert in cc)
            {
                cert.Subject.Should().Be("CN=leafclient");
                var encryptValue = cert.GetRSAPublicKey()!.Encrypt(Encoding.UTF8.GetBytes("test123"), RSAEncryptionPadding.OaepSHA1);
                var decryptValue = cert.GetRSAPrivateKey()!.Decrypt(encryptValue, RSAEncryptionPadding.OaepSHA1);
                Encoding.UTF8.GetString(decryptValue).Should().Be("test123");
            }

#if NET8_0_OR_GREATER
            // net6.0 does not support intermediate certs
            Assert.Equal(2, certs.ClientCertContext.IntermediateCertificates.Count);
            certs.ClientCertContext.IntermediateCertificates[0].Subject.Should().Be("CN=intermediate02");
            certs.ClientCertContext.IntermediateCertificates[1].Subject.Should().Be("CN=intermediate01");
#endif
        }
    }

    [SkippableTheory]
    [InlineData("resources/certs/client-cert.pem", "resources/certs/client-key.pem", 6)]
    [InlineData("resources/certs/chainedclient-cert.pem", "resources/certs/chainedclient-key.pem", 8)]
    public async Task Client_connect(string clientCertFile, string clientKeyFile, int minimumFrameworkVersion)
    {
        var version = int.Parse(Regex.Match(RuntimeInformation.FrameworkDescription, @"(\d+)\.\d").Groups[1].Value);
        Skip.IfNot(version >= minimumFrameworkVersion, $"Requires .NET {minimumFrameworkVersion}");

        const string caFile = "resources/certs/ca-cert.pem";

        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tls, tlsVerify: true)
                .Build());

        // Using files
        await Validate(server, new NatsTlsOpts
        {
            CaFile = caFile,
            CertFile = clientCertFile,
            KeyFile = clientKeyFile,
        });

        if (minimumFrameworkVersion < 8)
        {
            // Using PEM strings with LoadClientCert
            await Validate(server, new NatsTlsOpts
            {
                LoadCaCerts = NatsTlsOpts.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile)),
                LoadClientCert = NatsTlsOpts.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile)),
            });
        }

#if NET8_0_OR_GREATER
        // Using PEM strings with LoadClientCertContext
        await Validate(server, new NatsTlsOpts
        {
            LoadCaCerts = NatsTlsOpts.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile)),
            LoadClientCertContext = NatsTlsOpts.LoadClientCertContextFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile)),
        });
#endif

        return;

        static async Task Validate(NatsServer natsServer, NatsTlsOpts opts)
        {
            // overwrite the entire TLS option, because NatsServer.Start comes with some defaults
            var clientOpts = natsServer.ClientOpts(NatsOpts.Default) with
            {
                TlsOpts = opts,
            };

            await using var nats = new NatsConnection(clientOpts);

            await nats.ConnectAsync();
            var rtt = await nats.PingAsync();
            Assert.True(rtt > TimeSpan.Zero);
        }
    }
}
