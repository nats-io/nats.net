#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Advanced;

public class SecurityPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.SecurityPage");

        {
            #region user-pass
            NatsOpts opts = new NatsOpts
            {
                AuthOpts = NatsAuthOpts.Default with
                {
                    Username = "bob",
                    Password = "s3cr3t",
                },
            };

            await using NatsClient nats = new NatsClient(opts);
            #endregion
        }

        {
            #region tls-implicit
            NatsOpts opts = new NatsOpts
            {
                TlsOpts = new NatsTlsOpts
                {
                    Mode = TlsMode.Implicit,
                },
            };

            await using NatsClient nats = new NatsClient(opts);
            #endregion
        }

        {
            #region tls-mutual
            NatsOpts opts = new NatsOpts
            {
                TlsOpts = new NatsTlsOpts
                {
                    CertFile = "path/to/cert.pem",
                    KeyFile = "path/to/key.pem",
                    CaFile = "path/to/ca.pem",
                },
            };

            await using NatsClient nats = new NatsClient(opts);
            #endregion
        }
    }
}
