namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    [Fact]
    public async Task HeaderParsingTest()
    {
        await using var server = NatsServer.Start(_output, _transportType);

        await using var nats = server.CreateClientConnection();

        var sync = 0;
        var signal1 = new WaitSignal<NatsMsg<int>>();
        var signal2 = new WaitSignal<NatsMsg<int>>();
        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(m =>
        {
            if (m.Data < 10)
            {
                Interlocked.Exchange(ref sync, m.Data);
                return;
            }

            if (m.Data == 100)
                signal1.Pulse(m);
            if (m.Data == 200)
                signal2.Pulse(m);
        });

        await Retry.Until(
            "subscription is active",
            () => Volatile.Read(ref sync) == 1,
            async () => await nats.PublishAsync("foo", 1));

        var headers = new NatsHeaders
        {
            ["Test-Header-Key"] = "test-header-value",
            ["Multi"] = new[] { "multi-value-0", "multi-value-1" },
        };
        Assert.False(headers.IsReadOnly);

        // Send with headers
        await nats.PublishAsync("foo", 100, headers: headers);

        Assert.True(headers.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() =>
        {
            headers["should-not-set"] = "value";
        });

        var msg1 = await signal1;
        Assert.Equal(100, msg1.Data);
        Assert.NotNull(msg1.Headers);
        Assert.Equal(2, msg1.Headers!.Count);

        Assert.True(msg1.Headers!.ContainsKey("Test-Header-Key"));
        Assert.Single(msg1.Headers["Test-Header-Key"].ToArray());
        Assert.Equal("test-header-value", msg1.Headers["Test-Header-Key"]);

        Assert.True(msg1.Headers!.ContainsKey("Multi"));
        Assert.Equal(2, msg1.Headers["Multi"].Count);
        Assert.Equal("multi-value-0", msg1.Headers["Multi"][0]);
        Assert.Equal("multi-value-1", msg1.Headers["Multi"][1]);

        // Send empty headers
        await nats.PublishAsync("foo", 200, headers: new NatsHeaders());

        var msg2 = await signal2;
        Assert.Equal(200, msg2.Data);
        Assert.NotNull(msg2.Headers);
        Assert.Empty(msg2.Headers!);

        await sub.DisposeAsync();
        await reg;
    }
}
