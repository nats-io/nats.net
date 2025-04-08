namespace NATS.Client.Core2.Tests;

using NATS.Client.Core2.Tests.ExtraUtils.FrameworkPolyfillExtensions;

[Collection("nats-server")]
public class MemoryTests
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public MemoryTests(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Netstandard_inbox_sub_memory_leak()
    {
        // Because weak table for netstandard2.0 was not maintained properly
        // (same as the other targets removing the dub)
        // for example, on .NET Framework, the weak reference was not
        // garbage collected correctly leading to memory leak.
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var subject = $"{prefix}.foo";
        var data = $"{subject}.data";
        var end = $"{subject}.end";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await nats.ConnectAsync();

        await using var sub = await nats.SubscribeCoreAsync<int>($"{subject}.>", cancellationToken: cancellationToken);
        var subt = Task.Run(
            async () =>
            {
                await foreach (var natsMsg in sub.Msgs.ReadAllAsync(cancellationToken: cancellationToken))
                {
                    if (natsMsg.Subject == end)
                    {
                        break;
                    }

                    await natsMsg.ReplyAsync(1, cancellationToken: cancellationToken);
                }
            },
            cancellationToken);

        var mem1 = GC.GetTotalMemory(true);
        _output.WriteLine($"Allocated1 {mem1,10:N0}");
        var mems = new List<long>();
        var allowedMax = mem1 * 2;
        for (var j = 0; j < 5; j++)
        {
            for (var i = 0; i < 10000; i++)
            {
                await nats.RequestAsync<int>(data, cancellationToken: cancellationToken);
            }

            var mem2 = GC.GetTotalMemory(true);
            _output.WriteLine($"Allocated2 {mem2,10:N0} {allowedMax,10:N0}");
            mems.Add(mem2);
        }

        var max = mems.Max();
        _output.WriteLine($"Max {max,10:N0}");
        Assert.True(max < allowedMax, "Memory usage exceeded the allowed limit.");

        await nats.PublishAsync(end, cancellationToken: cancellationToken);
        await subt;
    }
}
