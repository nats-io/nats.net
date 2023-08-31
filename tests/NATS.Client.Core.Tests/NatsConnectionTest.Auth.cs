namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    public static IEnumerable<object[]> GetAuthConfigs()
    {
        yield return new object[]
        {
            new Auth(
                "TOKEN",
                "resources/configs/auth/token.conf",
                NatsOpts.Default with { AuthOpts = NatsAuthOpts.Default with { Token = "s3cr3t", }, }),
        };

        yield return new object[]
        {
            new Auth(
                "USER-PASSWORD",
                "resources/configs/auth/password.conf",
                NatsOpts.Default with
                {
                    AuthOpts = NatsAuthOpts.Default with { Username = "a", Password = "b", },
                }),
        };

        yield return new object[]
        {
            new Auth(
                "NKEY",
                "resources/configs/auth/nkey.conf",
                NatsOpts.Default with
                {
                    AuthOpts = NatsAuthOpts.Default with
                    {
                        NKey = "UALQSMXRSAA7ZXIGDDJBJ2JOYJVQIWM3LQVDM5KYIPG4EP3FAGJ47BOJ",
                        Seed = "SUAAVWRZG6M5FA5VRRGWSCIHKTOJC7EWNIT4JV3FTOIPO4OBFR5WA7X5TE",
                    },
                }),
        };

        yield return new object[]
        {
            new Auth(
                "NKEY (FROM FILE)",
                "resources/configs/auth/nkey.conf",
                NatsOpts.Default with
                {
                    AuthOpts = NatsAuthOpts.Default with { NKeyFile = "resources/configs/auth/user.nk", },
                }),
        };

        yield return new object[]
        {
            new Auth(
                "USER-CREDS",
                "resources/configs/auth/operator.conf",
                NatsOpts.Default with
                {
                    AuthOpts = NatsAuthOpts.Default with
                    {
                        Jwt =
                        "eyJ0eXAiOiJKV1QiLCJhbGciOiJlZDI1NTE5LW5rZXkifQ.eyJqdGkiOiJOVDJTRkVIN0pNSUpUTzZIQ09GNUpYRFNDUU1WRlFNV0MyWjI1TFk3QVNPTklYTjZFVlhBIiwiaWF0IjoxNjc5MTQ0MDkwLCJpc3MiOiJBREpOSlpZNUNXQlI0M0NOSzJBMjJBMkxPSkVBSzJSS1RaTk9aVE1HUEVCRk9QVE5FVFBZTUlLNSIsIm5hbWUiOiJteS11c2VyIiwic3ViIjoiVUJPWjVMUVJPTEpRRFBBQUNYSk1VRkJaS0Q0R0JaSERUTFo3TjVQS1dSWFc1S1dKM0VBMlc0UloiLCJuYXRzIjp7InB1YiI6e30sInN1YiI6e30sInN1YnMiOi0xLCJkYXRhIjotMSwicGF5bG9hZCI6LTEsInR5cGUiOiJ1c2VyIiwidmVyc2lvbiI6Mn19.ElYEknDixe9pZdl55S9PjduQhhqR1OQLglI1JO7YK7ECYb1mLUjGd8ntcR7ISS04-_yhygSDzX8OS8buBIxMDA",
                        Seed = "SUAJR32IC6D45J3URHJ5AOQZWBBO6QTID27NZQKXE3GC5U3SPFEYDJK6RQ",
                    },
                }),
        };

        yield return new object[]
        {
            new Auth(
                "USER-CREDS (FROM FILE)",
                "resources/configs/auth/operator.conf",
                NatsOpts.Default with
                {
                    AuthOpts = NatsAuthOpts.Default with { CredsFile = "resources/configs/auth/user.creds", },
                }),
        };
    }

    [Theory]
    [MemberData(nameof(GetAuthConfigs))]
    public async Task UserCredentialAuthTest(Auth auth)
    {
        var name = auth.Name;
        var serverConfig = auth.ServerConfig;
        var clientOpts = auth.ClientOpts;

        _output.WriteLine($"AUTH TEST {name}");

        var serverOpts = new NatsServerOptsBuilder()
            .UseTransport(_transportType)
            .AddServerConfig(serverConfig)
            .Build();

        await using var server = NatsServer.Start(_output, serverOpts, clientOpts);

        var subject = Guid.NewGuid().ToString("N");

        _output.WriteLine("TRY ANONYMOUS CONNECTION");
        {
            await using var failConnection = server.CreateClientConnection(ignoreAuthorizationException: true);
            var natsException =
                await Assert.ThrowsAsync<NatsException>(async () => await failConnection.PublishAsync(subject, 0));
            Assert.Contains("Authorization Violation", natsException.GetBaseException().Message);
        }

        await using var subConnection = server.CreateClientConnection(clientOpts);
        await using var pubConnection = server.CreateClientConnection(clientOpts);

        var signalComplete1 = new WaitSignal();
        var signalComplete2 = new WaitSignal();

        var syncCount = 0;
        var natsSub = await subConnection.SubscribeAsync<int>(subject);
        var register = natsSub.Register(x =>
        {
            Interlocked.Increment(ref syncCount);
            _output.WriteLine($"Received: {x}");
            if (x.Data == 1)
                signalComplete1.Pulse();
            if (x.Data == 2)
                signalComplete2.Pulse();
        });

        var syncCount1 = Volatile.Read(ref syncCount);
        await Retry.Until(
            "subscribed",
            () => syncCount1 != Volatile.Read(ref syncCount),
            async () => await pubConnection.PublishAsync(subject, 0));

        _output.WriteLine("AUTHENTICATED CONNECTION");
        await pubConnection.PublishAsync(subject, 1);
        await signalComplete1;

        var disconnectSignal1 = subConnection.ConnectionDisconnectedAsAwaitable();
        var disconnectSignal2 = pubConnection.ConnectionDisconnectedAsAwaitable();

        _output.WriteLine("TRY DISCONNECT START");
        await server.DisposeAsync(); // disconnect server
        await disconnectSignal1;
        await disconnectSignal2;

        _output.WriteLine("START NEW SERVER");
        await using var newServer = NatsServer.Start(_output, serverOpts, clientOpts);
        await subConnection.ConnectAsync(); // wait open again
        await pubConnection.ConnectAsync(); // wait open again

        _output.WriteLine("AUTHENTICATED RE-CONNECTION");

        var syncCount2 = Volatile.Read(ref syncCount);
        await Retry.Until(
            "re-subscribed",
            () => syncCount2 != Volatile.Read(ref syncCount),
            async () => await pubConnection.PublishAsync(subject, 0));

        await pubConnection.PublishAsync(subject, 2);
        await signalComplete2;

        await natsSub.DisposeAsync();
        await register;
    }

    public class Auth
    {
        public Auth(string name, string serverConfig, NatsOpts clientOpts)
        {
            Name = name;
            ServerConfig = serverConfig;
            ClientOpts = clientOpts;
        }

        public string Name { get; }

        public string ServerConfig { get; }

        public NatsOpts ClientOpts { get; }

        public override string ToString() => Name;
    }
}
