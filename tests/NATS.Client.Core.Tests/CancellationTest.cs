using System.Text;

namespace NATS.Client.Core.Tests;

public class CancellationTest
{
    private readonly ITestOutputHelper _output;

    public CancellationTest(ITestOutputHelper output) => _output = output;

    // should check
    // timeout via command-timeout(request-timeout)
    // timeout via connection dispose
    // cancel manually
    [Fact]
    public async Task CommandTimeoutTest()
    {
        var server = NatsServer.Start(_output, TransportType.Tcp);

        await using var conn = server.CreateClientConnection(NatsOpts.Default with { CommandTimeout = TimeSpan.FromMilliseconds(100) });
        await conn.ConnectAsync();

        // kill the server
        await server.DisposeAsync();

        // commands time out
        await Assert.ThrowsAsync<TimeoutException>(() => conn.PingAsync().AsTask());
        await Assert.ThrowsAsync<TimeoutException>(() => conn.PublishAsync("test").AsTask());
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var unused in conn.SubscribeAsync<string>("test"))
            {
            }
        });
    }

    // Queue-full

    // External Cancellation
}
