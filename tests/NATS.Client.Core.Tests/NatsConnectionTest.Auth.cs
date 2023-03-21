using System.Text.RegularExpressions;

namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    public static IEnumerable<object[]> GetAuthConfigs()
    {
        yield return new object[]
        {
            "TOKEN",
            "resources/configs/auth/token.conf",
            NatsOptions.Default with
            {
                AuthOptions = NatsAuthOptions.Default with
                {
                    Token = "s3cr3t"
                }
            }
        };

        yield return new object[]
        {
            "USER-PASSWORD",
            "resources/configs/auth/password.conf",
            NatsOptions.Default with
            {
                AuthOptions = NatsAuthOptions.Default with
                {
                    Username = "a",
                    Password = "b"
                }
            }
        };

        yield return new object[]
        {
            "NKEY",
            "resources/configs/auth/nkey.conf",
            NatsOptions.Default with
            {
                AuthOptions = NatsAuthOptions.Default with
                {
                    Nkey = "UALQSMXRSAA7ZXIGDDJBJ2JOYJVQIWM3LQVDM5KYIPG4EP3FAGJ47BOJ",
                    Seed = "SUAAVWRZG6M5FA5VRRGWSCIHKTOJC7EWNIT4JV3FTOIPO4OBFR5WA7X5TE"
                }
            }
        };

        yield return new object[]
        {
            "NKEY (FROM FILE)",
            "resources/configs/auth/nkey.conf",
            NatsOptions.Default with
            {
                AuthOptions = NatsAuthOptions.Default with
                {
                    NKeyFile = "resources/configs/auth/user.nk"
                }
            }
        };

        yield return new object[]
        {
            "USER-CREDS",
            "resources/configs/auth/operator.conf",
            NatsOptions.Default with
            {
                AuthOptions = NatsAuthOptions.Default with
                {
                    Jwt =
                    "eyJ0eXAiOiJKV1QiLCJhbGciOiJlZDI1NTE5LW5rZXkifQ.eyJqdGkiOiJOVDJTRkVIN0pNSUpUTzZIQ09GNUpYRFNDUU1WRlFNV0MyWjI1TFk3QVNPTklYTjZFVlhBIiwiaWF0IjoxNjc5MTQ0MDkwLCJpc3MiOiJBREpOSlpZNUNXQlI0M0NOSzJBMjJBMkxPSkVBSzJSS1RaTk9aVE1HUEVCRk9QVE5FVFBZTUlLNSIsIm5hbWUiOiJteS11c2VyIiwic3ViIjoiVUJPWjVMUVJPTEpRRFBBQUNYSk1VRkJaS0Q0R0JaSERUTFo3TjVQS1dSWFc1S1dKM0VBMlc0UloiLCJuYXRzIjp7InB1YiI6e30sInN1YiI6e30sInN1YnMiOi0xLCJkYXRhIjotMSwicGF5bG9hZCI6LTEsInR5cGUiOiJ1c2VyIiwidmVyc2lvbiI6Mn19.ElYEknDixe9pZdl55S9PjduQhhqR1OQLglI1JO7YK7ECYb1mLUjGd8ntcR7ISS04-_yhygSDzX8OS8buBIxMDA",
                    Seed = "SUAJR32IC6D45J3URHJ5AOQZWBBO6QTID27NZQKXE3GC5U3SPFEYDJK6RQ"
                }
            }
        };

        yield return new object[]
        {
            "USER-CREDS (FROM FILE)",
            "resources/configs/auth/operator.conf",
            NatsOptions.Default with
            {
                AuthOptions = NatsAuthOptions.Default with
                {
                    CredsFile = "resources/configs/auth/user.creds"
                }
            }
        };
    }

    [Theory]
    [MemberData(nameof(GetAuthConfigs))]
    public async Task UserCredentialAuthTest(string name, string serverConfig, NatsOptions clientOptions)
    {
        output.WriteLine($"AUTH TEST {name}");

        var serverOptions = new NatsServerOptionsBuilder()
            .UseTransport(transportType)
            .AddServerConfig(serverConfig)
            .Build();

        await using var server = new NatsServer(output, transportType, serverOptions);

        var key = new NatsKey(Guid.NewGuid().ToString("N"));

        output.WriteLine("TRY ANONYMOUS CONNECTION");
        {
            await using var failConnection = server.CreateClientConnection();
            var natsException =
                await Assert.ThrowsAsync<NatsException>(async () => await failConnection.PublishAsync(key, 0));
            Assert.Contains("Authorization Violation", natsException.GetBaseException().Message);
        }

        await using var subConnection = server.CreateClientConnection(clientOptions);
        await using var pubConnection = server.CreateClientConnection(clientOptions);

        var signalComplete1 = new WaitSignal();
        var signalComplete2 = new WaitSignal();

        await subConnection.SubscribeAsync<int>(key, x =>
        {
            output.WriteLine($"Received: {x}");
            if (x == 1) signalComplete1.Pulse();
            if (x == 2) signalComplete2.Pulse();
        });
        await subConnection.PingAsync(); // wait for subscribe complete

        output.WriteLine("AUTHENTICATED CONNECTION");
        await pubConnection.PublishAsync(key, 1);
        await signalComplete1;

        var disconnectSignal1 = subConnection.ConnectionDisconnectedAsAwaitable();
        var disconnectSignal2 = pubConnection.ConnectionDisconnectedAsAwaitable();

        output.WriteLine("TRY DISCONNECT START");
        await server.DisposeAsync(); // disconnect server
        await disconnectSignal1;
        await disconnectSignal2;

        output.WriteLine("START NEW SERVER");
        await using var newServer = new NatsServer(output, transportType, serverOptions);
        await subConnection.ConnectAsync(); // wait open again
        await pubConnection.ConnectAsync(); // wait open again

        output.WriteLine("AUTHENTICATED RE-CONNECTION");
        await pubConnection.PublishAsync(key, 2);
        await signalComplete2;
    }
}
