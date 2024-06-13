using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace NATS.Client.Core.Tests;

public class TlsOptsTest
{
    [Fact]
    public async Task Load_ca_cert()
    {
        const string caFile = "resources/certs/ca-cert.pem";

        await ValidateAsync(new NatsTlsOpts
        {
            CaFile = caFile,
        });

        await ValidateAsync(new NatsTlsOpts
        {
#pragma warning disable CS0618 // Type or member is obsolete
            LoadCaCerts = NatsTlsOpts.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile)),
#pragma warning restore CS0618 // Type or member is obsolete
        });

        await ValidateAsync(new NatsTlsOpts
        {
            ConfigureClientAuthentication = async options =>
            {
                options.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile));
            },
        });

        return;

        static async ValueTask ValidateAsync(NatsTlsOpts opts)
        {
            var clientOpts = await opts.AuthenticateAsClientOptionsAsync(new NatsUri("demo.nats.io", true));
            Assert.NotNull(clientOpts.RemoteCertificateValidationCallback);
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
#pragma warning disable CS0618 // Type or member is obsolete
            LoadClientCert = NatsTlsOpts.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile)),
#pragma warning restore CS0618 // Type or member is obsolete
        });

        await ValidateAsync(new NatsTlsOpts
        {
            ConfigureClientAuthentication = async options =>
            {
                options.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile));
            },
        });

        return;

        static async Task ValidateAsync(NatsTlsOpts opts)
        {
            var clientOpts = await opts.AuthenticateAsClientOptionsAsync(new NatsUri("demo.nats.io", true));

#if NET8_0_OR_GREATER
            Assert.NotNull(clientOpts.ClientCertificateContext);
            var leafCert = clientOpts.ClientCertificateContext.TargetCertificate;

            leafCert.Subject.Should().Be("CN=client");
            var encryptValue = leafCert.GetRSAPublicKey()!.Encrypt(Encoding.UTF8.GetBytes("test123"), RSAEncryptionPadding.OaepSHA1);
            var decryptValue = leafCert.GetRSAPrivateKey()!.Decrypt(encryptValue, RSAEncryptionPadding.OaepSHA1);
            Encoding.UTF8.GetString(decryptValue).Should().Be("test123");
#else
            Assert.NotNull(clientOpts.ClientCertificates);
            Assert.Single(clientOpts.ClientCertificates);
#endif
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

        await ValidateAsync(new NatsTlsOpts
        {
            ConfigureClientAuthentication = async options =>
            {
                options.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile));
            },
        });

        return;

        static async Task ValidateAsync(NatsTlsOpts opts)
        {
#if NET8_0_OR_GREATER
            var clientOpts = await opts.AuthenticateAsClientOptionsAsync(new NatsUri("demo.nats.io", true));
            var ctx = clientOpts.ClientCertificateContext;
            Assert.NotNull(ctx);

            var leafCert = ctx.TargetCertificate;
            leafCert.Subject.Should().Be("CN=leafclient");
            var encryptValue = leafCert.GetRSAPublicKey()!.Encrypt(Encoding.UTF8.GetBytes("test123"), RSAEncryptionPadding.OaepSHA1);
            var decryptValue = leafCert.GetRSAPrivateKey()!.Decrypt(encryptValue, RSAEncryptionPadding.OaepSHA1);
            Encoding.UTF8.GetString(decryptValue).Should().Be("test123");

            Assert.Equal(2, ctx.IntermediateCertificates.Count);
            ctx.IntermediateCertificates[0].Subject.Should().Be("CN=intermediate02");
            ctx.IntermediateCertificates[1].Subject.Should().Be("CN=intermediate01");
#else
            await Assert.ThrowsAsync<NotSupportedException>(async () => await opts.AuthenticateAsClientOptionsAsync(new NatsUri("demo.nats.io", true)));
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
        await ValidateAsync(server, new NatsTlsOpts
        {
            CaFile = caFile,
            CertFile = clientCertFile,
            KeyFile = clientKeyFile,
        });

        if (minimumFrameworkVersion < 8)
        {
            // Using callbacks
            await ValidateAsync(server, new NatsTlsOpts
            {
#pragma warning disable CS0618 // Type or member is obsolete
                LoadCaCerts = NatsTlsOpts.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile)),
                LoadClientCert = NatsTlsOpts.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile)),
#pragma warning restore CS0618 // Type or member is obsolete
            });
        }

        // Using ConfigureClientAuthentication
        await ValidateAsync(server, new NatsTlsOpts
        {
            ConfigureClientAuthentication = async options =>
            {
                options.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile));
                options.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile));
            },
        });

        return;

        static async Task ValidateAsync(NatsServer natsServer, NatsTlsOpts opts)
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
