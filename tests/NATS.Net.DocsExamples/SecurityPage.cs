#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Net.DocsExamples;

public class SecurityPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.SecurityPage");

        {
            #region user-pass
            var opts = NatsOpts.Default with
            {
                AuthOpts = NatsAuthOpts.Default with
                {
                    Username = "bob",
                    Password = "s3cr3t",
                },
            };

            await using var nats = new NatsConnection(opts);
            #endregion
        }

        {
            #region tls-implicit
            var opts = NatsOpts.Default with
            {
                TlsOpts = new NatsTlsOpts
                {
                    Mode = TlsMode.Implicit,
                },
            };

            await using var nats = new NatsConnection(opts);
            #endregion
        }

        {
            #region tls-mutual
            var opts = NatsOpts.Default with
            {
                TlsOpts = new NatsTlsOpts
                {
                    CertFile = "path/to/cert.pem",
                    KeyFile = "path/to/key.pem",
                    CaFile = "path/to/ca.pem",
                },
            };

            await using var nats = new NatsConnection(opts);
            #endregion
        }
    }
}
