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

    [Theory]
    [InlineData("client-cert-bundle.pfx", null, "client-key.pem", null)]
    [InlineData("client-cert-bundle.pfx", "", "client-key.pem", "")]
    [InlineData("client-cert-bundle-pass.pfx", "1234", "client-key-pass.pem", "5678")]
    public async Task Load_client_cert_and_key(string pfxFile, string? pfxFilepassword, string keyFile, string? keyFilePassword)
    {
        const string clientCertFile = "resources/certs/client-cert.pem";
        var clientKeyFile = $"resources/certs/{keyFile}";
        var clientCertBundleFile = $"resources/certs/{pfxFile}";

        await ValidateAsync(new NatsTlsOpts
        {
            CertFile = clientCertFile,
            KeyFile = clientKeyFile,
            KeyFilePassword = keyFilePassword,
        });

        await ValidateAsync(new NatsTlsOpts
        {
            CertBundleFile = clientCertBundleFile,
            CertBundleFilePassword = pfxFilepassword,
        });

        await ValidateAsync(new NatsTlsOpts
        {
            ConfigureClientAuthentication = async options =>
            {
                options.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile), password: keyFilePassword);
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
        const string clientCertBundleFile = "resources/certs/chainedclient-cert-bundle.pfx";
        const string clientKeyFile = "resources/certs/chainedclient-key.pem";

        await ValidateAsync(new NatsTlsOpts
        {
            CertFile = clientCertFile,
            KeyFile = clientKeyFile,
        });

        await ValidateAsync(new NatsTlsOpts
        {
            CertBundleFile = clientCertBundleFile,
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

    [Theory]
    [InlineData("resources/certs/client-cert.pem", "resources/certs/client-key.pem", null, 6)]
    [InlineData(null, null, "resources/certs/client-cert-bundle.pfx", 6)]
    [InlineData("resources/certs/chainedclient-cert.pem", "resources/certs/chainedclient-key.pem", null, 8)]
    [InlineData(null, null, "resources/certs/chainedclient-cert-bundle.pfx", 8)]
    public async Task Client_connect(string? clientCertFile, string? clientKeyFile, string? clientCertBundleFile, int minimumFrameworkVersion)
    {
        var version = int.Parse(Regex.Match(RuntimeInformation.FrameworkDescription, @"(\d+)\.\d").Groups[1].Value);
        Assert.SkipUnless(version >= minimumFrameworkVersion, $"Requires .NET {minimumFrameworkVersion}");

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
            CertBundleFile = clientCertBundleFile,
        });

        // Using ConfigureClientAuthentication
        await ValidateAsync(server, new NatsTlsOpts
        {
            ConfigureClientAuthentication = async options =>
            {
                options.LoadCaCertsFromPem(await File.ReadAllTextAsync(caFile));
                if (clientCertFile != default && clientKeyFile != default)
                {
                    options.LoadClientCertFromPem(await File.ReadAllTextAsync(clientCertFile), await File.ReadAllTextAsync(clientKeyFile));
                }

                if (clientCertBundleFile != default)
                {
                    options.LoadClientCertFromPfxFile(clientCertBundleFile);
                }
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
