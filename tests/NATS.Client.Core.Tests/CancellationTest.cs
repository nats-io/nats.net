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

        await subConnection.SubscribeAsync("foo");

        var cmd = new SleepWriteCommand("PUB foo 5\r\naiueo", TimeSpan.FromSeconds(10));
        pubConnection.PostDirectWrite(cmd);

        var timeoutException = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pubConnection.PublishAsync("foo", "aiueo", opts: new NatsPubOpts { WaitUntilSent = true });
        });

        timeoutException.Message.Should().Contain("1 seconds elapsing");
    }

    // Queue-full

    // External Cancellation
}

// writer queue can't consume when sleeping
internal class SleepWriteCommand : ICommand
{
    private readonly byte[] _protocol;
    private readonly TimeSpan _sleepTime;

    public SleepWriteCommand(string protocol, TimeSpan sleepTime)
    {
        _protocol = Encoding.UTF8.GetBytes(protocol + "\r\n");
        _sleepTime = sleepTime;
    }

    public bool IsCanceled => false;

    public void Return(ObjectPool pool)
    {
    }

    public void SetCancellationTimer(CancellationTimer timer)
    {
    }

    public void Write(ProtocolWriter writer)
    {
        Thread.Sleep(_sleepTime);
        writer.WriteRaw(_protocol);
    }
}
