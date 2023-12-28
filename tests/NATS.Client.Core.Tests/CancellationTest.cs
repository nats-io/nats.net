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
        await using var server = NatsServer.Start(_output, TransportType.Tcp);

        await using var subConnection = server.CreateClientConnection(NatsOpts.Default with { CommandTimeout = TimeSpan.FromSeconds(1) });
        await using var pubConnection = server.CreateClientConnection(NatsOpts.Default with { CommandTimeout = TimeSpan.FromSeconds(1) });
        await pubConnection.ConnectAsync();

        await subConnection.SubscribeCoreAsync<string>("foo");

        var task = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            // todo: test this by disconnecting the server
            // await pubConnection.DirectWriteAsync("PUB foo 5\r\naiueo");
        });

        var timeoutException = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pubConnection.PublishAsync("foo", "aiueo", opts: new NatsPubOpts { WaitUntilSent = true });
        });

        timeoutException.Message.Should().Contain("1 seconds elapsing");
        await task;
    }

    // Queue-full

    // External Cancellation
}
