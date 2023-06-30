namespace NATS.Client.Core.Tests;

public class ProtocolTest
{
    private readonly ITestOutputHelper _output;

    public ProtocolTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Subscription_with_same_subject()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp);
        var nats1 = server.CreateClientConnection();
        var (nats2, proxy) = server.CreateProxiedClientConnection();

        var sub1 = await nats2.SubscribeAsync<int>("foo.bar");
        var sub2 = await nats2.SubscribeAsync<int>("foo.bar");
        var sub3 = await nats2.SubscribeAsync<int>("foo.baz");

        var sync1 = 0;
        var sync2 = 0;
        var sync3 = 0;
        var count = new WaitSignal(3);

        var reg1 = sub1.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync1, 1);
                return;
            }

            count.Pulse(m.Subject == "foo.bar" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });

        var reg2 = sub2.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync2, 1);
                return;
            }

            count.Pulse(m.Subject == "foo.bar" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });

        var reg3 = sub3.Register(m =>
        {
            if (m.Data == 0)
            {
                Interlocked.Exchange(ref sync3, 1);
                return;
            }

            count.Pulse(m.Subject == "foo.baz" ? null : new Exception($"Subject mismatch {m.Subject}"));
        });

        // Since subscription and publishing are sent through different connections there is
        // a race where one or more subscriptions are made after the publishing happens.
        // So, we make sure subscribers are accepted by the server before we send any test data.
        await Retry.Until(
            "all subscriptions are active",
            () => Volatile.Read(ref sync1) + Volatile.Read(ref sync2) + Volatile.Read(ref sync3) == 3,
            async () =>
            {
                await nats1.PublishAsync("foo.bar", 0);
                await nats1.PublishAsync("foo.baz", 0);
            });

        await nats1.PublishAsync("foo.bar", 1);
        await nats1.PublishAsync("foo.baz", 1);

        // Wait until we received all test data
        await count;

        var frames = proxy.ClientFrames.OrderBy(f => f.Message).ToList();

        foreach (var frame in frames)
        {
            _output.WriteLine($"[PROXY] {frame}");
        }

        Assert.Equal(3, frames.Count);
        Assert.StartsWith("SUB foo.bar", frames[0].Message);
        Assert.StartsWith("SUB foo.bar", frames[1].Message);
        Assert.StartsWith("SUB foo.baz", frames[2].Message);
        Assert.False(frames[0].Message.Equals(frames[1].Message), "Should have different SIDs");

        await sub1.DisposeAsync();
        await reg1;
        await sub2.DisposeAsync();
        await reg2;
        await sub3.DisposeAsync();
        await reg3;
        await nats1.DisposeAsync();
        await nats2.DisposeAsync();
        proxy.Dispose();
    }

    [Fact]
    public async Task Publish_empty_message_for_notifications()
    {
        await using var server = new NatsServer(_output, TransportType.Tcp);
        var (nats, proxy) = server.CreateProxiedClientConnection();

        var sync = 0;
        var signal1 = new WaitSignal<NatsMsg>();
        var signal2 = new WaitSignal<NatsMsg>();
        var sub = await nats.SubscribeAsync("foo.*");
        var reg = sub.Register(m =>
        {
            switch (m.Subject)
            {
            case "foo.sync":
                Interlocked.Exchange(ref sync, 1);
                break;
            case "foo.signal1":
                signal1.Pulse(m);
                break;
            case "foo.signal2":
                signal2.Pulse(m);
                break;
            }
        });

        await Retry.Until(
            "subscription is active",
            () => Volatile.Read(ref sync) == 1,
            async () => await nats.PublishAsync("foo.sync"),
            retryDelay: TimeSpan.FromSeconds(1));

        // PUB notifications
        await nats.PublishAsync("foo.signal1");
        var msg1 = await signal1;
        Assert.Equal(0, msg1.Data.Length);
        Assert.Null(msg1.Headers);
        var pubFrame1 = proxy.Frames.First(f => f.Message.StartsWith("PUB foo.signal1"));
        Assert.Equal("PUB foo.signal1 0␍␊", pubFrame1.Message);
        var msgFrame1 = proxy.Frames.First(f => f.Message.StartsWith("MSG foo.signal1"));
        Assert.Matches(@"^MSG foo.signal1 \w+ 0␍␊$", msgFrame1.Message);

        // HPUB notifications
        await nats.PublishAsync("foo.signal2", opts: new NatsPubOpts { Headers = new NatsHeaders() });
        var msg2 = await signal2;
        Assert.Equal(0, msg2.Data.Length);
        Assert.NotNull(msg2.Headers);
        Assert.Empty(msg2.Headers!);
        var pubFrame2 = proxy.Frames.First(f => f.Message.StartsWith("HPUB foo.signal2"));
        Assert.Equal("HPUB foo.signal2 12 12␍␊NATS/1.0␍␊␍␊", pubFrame2.Message);
        var msgFrame2 = proxy.Frames.First(f => f.Message.StartsWith("HMSG foo.signal2"));
        Assert.Matches(@"^HMSG foo.signal2 \w+ 12 12␍␊NATS/1.0␍␊␍␊$", msgFrame2.Message);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Unsubscribe_max_msgs()
    {
        // Use a single server to test multiple scenarios to make test runs more efficient
        await using var server = new NatsServer();
        var (nats, proxy) = server.CreateProxiedClientConnection();

        // Auto-unsubscribe after consuming max-msgs
        {
            const int maxMsgs = 99;
            var opts = new NatsSubOpts { MaxMsgs = maxMsgs };
            await using var sub = await nats.SubscribeAsync<int>("foo", opts);

            var sid = ((INatsSub)sub).Sid;
            await Retry.Until("all frames arrived", () => proxy.Frames.Count >= 2);
            Assert.Equal($"SUB foo {sid}", proxy.Frames[0].Message);
            Assert.Equal($"UNSUB {sid} {maxMsgs}", proxy.Frames[1].Message);

            // send more messages than max to check we only get max
            for (var i = 0; i < maxMsgs + 10; i++)
            {
                await nats.PublishAsync("foo", i);
            }

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var count = 0;
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync(cancellationToken))
            {
                Assert.Equal(count, natsMsg.Data);
                count++;
            }

            Assert.Equal(maxMsgs, count);
            Assert.Equal(NatsSubEndReason.MaxMsgs, sub.EndReason);
        }

        // Manual unsubscribe
        {
            await proxy.FlushFramesAsync(nats);

            await using var sub = await nats.SubscribeAsync<int>("foo2");

            await sub.UnsubscribeAsync();

            var sid = ((INatsSub)sub).Sid;

            await Retry.Until("all frames arrived", () => proxy.ClientFrames.Count == 2);

            Assert.Equal($"SUB foo2 {sid}", proxy.ClientFrames[0].Message);
            Assert.Equal($"UNSUB {sid}", proxy.ClientFrames[1].Message);

            // send messages to check we receive none since we're already unsubscribed
            for (var i = 0; i < 100; i++)
            {
                await nats.PublishAsync("foo2", i);
            }

            await Retry.Until("all pub frames arrived", () => proxy.Frames.Count(f => f.Message.StartsWith("PUB foo2")) == 100);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var count = 0;
            await foreach (var unused in sub.Msgs.ReadAllAsync(cancellationToken))
            {
                count++;
            }

            Assert.Equal(0, count);
            Assert.Equal(NatsSubEndReason.None, sub.EndReason);
        }

        // Reconnect
        {
            proxy.Reset();

            const int maxMsgs = 100;
            const int pubMsgs = 10;
            var opts = new NatsSubOpts { MaxMsgs = maxMsgs };
            var sub = await nats.SubscribeAsync<int>("foo3", opts);
            var count = 0;
            var reg = sub.Register(_ => Interlocked.Increment(ref count));
            var sid = ((INatsSub)sub).Sid;
            await Retry.Until("subscribed", () => proxy.Frames.Any(f => f.Message == $"SUB foo3 {sid}"));

            for (var i = 0; i < pubMsgs; i++)
            {
                await nats.PublishAsync("foo3", i);
            }

            await Retry.Until("published", () => proxy.Frames.Count(f => f.Message.StartsWith("PUB foo3")) == 10);
            await Retry.Until("received", () => Volatile.Read(ref count) == 10);

            var pending = maxMsgs - pubMsgs;
            Assert.Equal(pending, ((INatsSub)sub).PendingMsgs);

            proxy.Reset();

            // SUB + UNSUB
            await Retry.Until("re-subscribed", () => proxy.ClientFrames.Count == 2);

            // Make sure we're still using the same SID
            Assert.Equal($"SUB foo3 {sid}", proxy.ClientFrames[0].Message);
            Assert.Equal($"UNSUB {sid} {pending}", proxy.ClientFrames[1].Message);

            // We already published a few, this should exceed max-msgs
            for (var i = 0; i < maxMsgs; i++)
            {
                await nats.PublishAsync("foo3", i);
            }

            await Retry.Until(
                "published more",
                () => proxy.ClientFrames.Count(f => f.Message.StartsWith("PUB foo3")) == maxMsgs);

            await Retry.Until(
                "unsubscribed with max-msgs",
                () => sub.EndReason == NatsSubEndReason.MaxMsgs);

            Assert.Equal(Volatile.Read(ref count), maxMsgs);

            await sub.DisposeAsync();
            await reg;
        }
    }
}
