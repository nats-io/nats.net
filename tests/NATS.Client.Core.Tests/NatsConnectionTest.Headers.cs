namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    [Fact]
    public async Task HeaderParsingTest()
    {
        await using var server = new NatsServer(_output, _transportType);

        await using var nats = server.CreateClientConnection();

        var sync = 0;
        var signal = new WaitSignal<NatsMsg<int>>();
        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync, 1);
                return;
            }

            signal.Pulse(m);
        });

        await Retry.Until(
            "subscription is active",
            () => Volatile.Read(ref sync) == 1,
            async () => await nats.PublishAsync("foo", 0));

        var headers = new NatsHeaders
        {
            ["Test-Header-Key"] = "test-header-value",
            ["Multi"] = new[] { "multi-value-0", "multi-value-1" },
        };
        Assert.False(headers.IsReadOnly);

        await nats.PublishAsync("foo", 1, new NatsPubOpts { Headers = headers });

        Assert.True(headers.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() =>
        {
            headers["should-not-set"] = "value";
        });

        var msg = await signal;
        Assert.Equal(1, msg.Data);
        Assert.NotNull(msg.Headers);
        Assert.Equal(2, msg.Headers!.Count);

        Assert.True(msg.Headers!.ContainsKey("Test-Header-Key"));
        Assert.Single(msg.Headers["Test-Header-Key"].ToArray());
        Assert.Equal("test-header-value", msg.Headers["Test-Header-Key"]);

        Assert.True(msg.Headers!.ContainsKey("Multi"));
        Assert.Equal(2, msg.Headers["Multi"].Count);
        Assert.Equal("multi-value-0", msg.Headers["Multi"][0]);
        Assert.Equal("multi-value-1", msg.Headers["Multi"][1]);

        await sub.DisposeAsync();
        await reg;
    }
}
