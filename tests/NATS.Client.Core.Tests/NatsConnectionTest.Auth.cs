namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    [Fact]
    public async Task UserCredentialAuthTest()
    {
        var serverOptions = new NatsServerOptionsBuilder()
            .UseTransport(transportType)
            .AddServerConfig("resources/configs/auth/my-operator.conf")
            .Build();

        await using var server = new NatsServer(output, transportType, serverOptions);

        var clientOptions = NatsOptions.Default with
        {
            AuthOptions = NatsAuthOptions.Default with
            {
                CredsFile = "resources/configs/auth/my-user.creds"
            }
        };

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
