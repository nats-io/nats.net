using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NATS.Client.Core.Tests;

public class TlsCertsTest
{
    [Fact]
    public void Load_ca_cert()
    {
        var caFile = "resources/certs/ca-cert.pem";
        var caPem = File.ReadAllText(caFile);

        // CA cert from file
        {
            var opts = new NatsTlsOpts { CaFile = caFile };
            var certs = new TlsCerts(opts);

            Assert.NotNull(certs.CaCerts);
            Assert.Single(certs.CaCerts);
            foreach (var cert in certs.CaCerts)
            {
                cert.Subject.Should().Be("CN=ca");
            }
        }

        // CA cert from PEM string
        {
            var opts = new NatsTlsOpts { CaFile = caPem };
            var certs = new TlsCerts(opts);

            Assert.NotNull(certs.CaCerts);
            Assert.Single(certs.CaCerts);
            foreach (var cert in certs.CaCerts)
            {
                cert.Subject.Should().Be("CN=ca");
            }
        }
    }

    [Fact]
    public void Load_client_cert_and_key()
    {
        var clientCertFile = "resources/certs/client-cert.pem";
        var clientKeyFile = "resources/certs/client-key.pem";
        var clientCertPem = File.ReadAllText(clientCertFile);
        var clientKeyPem = File.ReadAllText(clientKeyFile);

        Validate(clientCertFile, clientKeyFile);
        Validate(clientCertPem, clientKeyPem);
        Validate(clientCertFile, clientKeyPem);
        Validate(clientCertPem, clientKeyFile);

        return;

        static void Validate(string cert, string key)
        {
            var opts = new NatsTlsOpts { CertFile = cert, KeyFile = key };
            var certs = new TlsCerts(opts);

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
    public async Task Client_connect()
    {
        await using var server = NatsServer.Start(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tls, tlsVerify: true)
                .Build());

        // Using files
        await Validate(
            server,
            ca: "resources/certs/ca-cert.pem",
            cert: "resources/certs/client-cert.pem",
            key: "resources/certs/client-key.pem");

        // Using PEM strings
        await Validate(
            server,
            ca: await File.ReadAllTextAsync("resources/certs/ca-cert.pem"),
            cert: await File.ReadAllTextAsync("resources/certs/client-cert.pem"),
            key: await File.ReadAllTextAsync("resources/certs/client-key.pem"));

        return;

        static async Task Validate(NatsServer natsServer, string ca, string cert, string key)
        {
            var clientOpts = natsServer.ClientOpts(NatsOpts.Default);

            clientOpts = clientOpts with
            {
                TlsOpts = clientOpts.TlsOpts with
                {
                    CaFile = ca, CertFile = cert, KeyFile = key,
                },
            };

            await using var nats = new NatsConnection(clientOpts);

            await nats.ConnectAsync();
            var rtt = await nats.PingAsync();
            Assert.True(rtt > TimeSpan.Zero);
        }
    }
}
