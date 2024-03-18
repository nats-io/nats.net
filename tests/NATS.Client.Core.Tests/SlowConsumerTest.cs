namespace NATS.Client.Core.Tests;

public class SlowConsumerTest
{
    private readonly ITestOutputHelper _output;

    public SlowConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Slow_consumer()
    {
        await using var server = NatsServer.Start();
        var nats = server.CreateClientConnection(new NatsOpts { SubPendingChannelCapacity = 3 });

        var lost = 0;
        nats.MessageDropped += (_, e) =>
        {
            if (e is { } dropped)
            {
                Interlocked.Increment(ref lost);
                _output.WriteLine($"LOST {dropped.Data}");
            }

            return default;
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var sync = 0;
        var end = 0;
        var count = 0;
        var signal = new WaitSignal();
        var subscription = Task.Run(
            async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<string>("foo.>", cancellationToken: cancellationToken))
                {
                    if (msg.Subject == "foo.sync")
                    {
                        Interlocked.Increment(ref sync);
                        continue;
                    }

                    if (msg.Subject == "foo.end")
                    {
                        Interlocked.Increment(ref end);
                        break;
                    }

                    await signal;

                    await Task.Delay(100, cancellationToken);

                    Interlocked.Increment(ref count);
                    _output.WriteLine($"GOOD {msg.Data}");
                }
            },
            cancellationToken);

        await Retry.Until(
            "subscription is ready",
            () => Volatile.Read(ref sync) > 0,
            async () => await nats.PublishAsync("foo.sync", cancellationToken: cancellationToken));

        for (var i = 0; i < 10; i++)
        {
            await nats.PublishAsync("foo.data", $"A{i}", cancellationToken: cancellationToken);
        }

        signal.Pulse();

        for (var i = 0; i < 10; i++)
        {
            await nats.PublishAsync("foo.data", $"B{i}", cancellationToken: cancellationToken);
        }

        await Retry.Until(
            "subscription is ended",
            () => Volatile.Read(ref end) > 0,
            async () => await nats.PublishAsync("foo.end", cancellationToken: cancellationToken));

        await subscription;

        // we should've lost most of the messages because of the low channel capacity
        Volatile.Read(ref count).Should().BeLessThan(20);
        Volatile.Read(ref lost).Should().BeGreaterThan(0);
    }
}
