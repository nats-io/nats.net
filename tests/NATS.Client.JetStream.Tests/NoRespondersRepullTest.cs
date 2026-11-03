using System.Collections.Concurrent;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

/// <summary>
/// The first pull to a just-created consumer can race the propagation of its interest across a cluster and get a 503
/// No Responders, so until the consumer has replied, a 503 is followed by a few quick re-pulls rather than waiting for
/// the idle heartbeat timer. After that, a 503 is left to the heartbeat timer as before.
/// </summary>
public class NoRespondersRepullTest
{
    // The first pull and the 8 quick re-pulls, which take at most about 2.5s
    private const int PullsBeforeQuickRepullsRunOut = 9;
    private static readonly TimeSpan QuickRepullsRunOut = TimeSpan.FromSeconds(4);

    // Long enough that waiting for the heartbeat timeout (2 x this) would fail the tests that mustn't
    private static readonly TimeSpan LongIdleHeartbeat = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Consume_pulls_again_promptly_after_503()
    {
        var pulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerInfo(cmd) || !IsPull(cmd))
                return Task.CompletedTask;

            if (Interlocked.Increment(ref pulls) == 1)
                cmd.Reply(headers: "NATS/1.0 503");
            else
                SendJsMsg(cmd);

            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().GetConsumerAsync("x", "c");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var msg in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 10, IdleHeartbeat = LongIdleHeartbeat }, cancellationToken: cts.Token))
        {
            Assert.Equal("1", msg.Data);
            break;
        }

        Assert.Equal(2, Volatile.Read(ref pulls));
    }

    [Fact]
    public async Task Consume_still_ends_on_the_first_503_when_MaxConsecutive503Errors_is_1()
    {
        var pulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerInfo(cmd) || !IsPull(cmd))
                return Task.CompletedTask;

            Interlocked.Increment(ref pulls);
            cmd.Reply(headers: "NATS/1.0 503");
            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().GetConsumerAsync("x", "c");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var opts = new NatsJSConsumeOpts { MaxMsgs = 10, IdleHeartbeat = LongIdleHeartbeat, MaxConsecutive503Errors = 1 };
        var exception = await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await foreach (var unused in consumer.ConsumeAsync<string>(opts: opts, cancellationToken: cts.Token))
            {
            }
        });

        Assert.Contains("503", exception.Message);
        Assert.Equal(1, Volatile.Read(ref pulls));
    }

    [Fact]
    public async Task Consume_does_not_count_the_503s_of_quick_repulls_towards_MaxConsecutive503Errors()
    {
        var pulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerInfo(cmd) || !IsPull(cmd))
                return Task.CompletedTask;

            Interlocked.Increment(ref pulls);
            cmd.Reply(headers: "NATS/1.0 503");
            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().GetConsumerAsync("x", "c");

        // The first 503 counts, the quick re-pulls' don't, and the heartbeat timer's pull (2s later) gets the second
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var opts = new NatsJSConsumeOpts { MaxMsgs = 10, IdleHeartbeat = TimeSpan.FromSeconds(1), MaxConsecutive503Errors = 2 };
        var exception = await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await foreach (var unused in consumer.ConsumeAsync<string>(opts: opts, cancellationToken: cts.Token))
            {
            }
        });

        Assert.Contains("503", exception.Message);
        Assert.Equal(PullsBeforeQuickRepullsRunOut + 1, Volatile.Read(ref pulls));
    }

    [Fact]
    public async Task Consume_leaves_a_503_to_the_heartbeat_timer_once_the_consumer_has_replied()
    {
        var pulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerInfo(cmd) || !IsPull(cmd))
                return Task.CompletedTask;

            // A message for the first pull, which takes pending to the threshold so the consumer pulls again
            if (Interlocked.Increment(ref pulls) == 1)
                SendJsMsg(cmd);
            else
                cmd.Reply(headers: "NATS/1.0 503");

            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().GetConsumerAsync("x", "c");

        using var cts = new CancellationTokenSource(QuickRepullsRunOut);
        var received = 0;
        try
        {
            await foreach (var unused in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 2, IdleHeartbeat = LongIdleHeartbeat }, cancellationToken: cts.Token))
                received++;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(1, received);
        Assert.Equal(2, Volatile.Read(ref pulls));
    }

    [Fact]
    public async Task Ordered_consume_pulls_again_promptly_after_503_without_recreating_the_consumer()
    {
        var created = new ConcurrentQueue<string>();
        var pulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerLifecycle(cmd, created) || !IsPull(cmd))
                return Task.CompletedTask;

            if (Interlocked.Increment(ref pulls) == 1)
                cmd.Reply(headers: "NATS/1.0 503");
            else
                SendJsMsg(cmd);

            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().CreateOrderedConsumerAsync("x");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var msg in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 10, IdleHeartbeat = LongIdleHeartbeat }, cancellationToken: cts.Token))
        {
            Assert.Equal("1", msg.Data);
            break;
        }

        Assert.Equal(2, Volatile.Read(ref pulls));
        Assert.Single(created);
    }

    [Fact]
    public async Task Ordered_consume_recreates_the_consumer_on_heartbeat_timeout_once_quick_repulls_run_out()
    {
        var created = new ConcurrentQueue<string>();
        var firstConsumerPulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerLifecycle(cmd, created) || !IsPull(cmd))
                return Task.CompletedTask;

            // The first consumer has vanished, so its pulls get no responders; its replacement works
            if (created.TryPeek(out var first) && ConsumerOf(cmd) == first)
            {
                Interlocked.Increment(ref firstConsumerPulls);
                cmd.Reply(headers: "NATS/1.0 503");
            }
            else
            {
                SendJsMsg(cmd);
            }

            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().CreateOrderedConsumerAsync("x");

        // A heartbeat timeout (2s) longer than the gaps between quick re-pulls, so it only fires once they've run out
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 10, IdleHeartbeat = TimeSpan.FromSeconds(1) }, cancellationToken: cts.Token))
        {
            Assert.Equal("1", msg.Data);
            break;
        }

        Assert.Equal(PullsBeforeQuickRepullsRunOut, Volatile.Read(ref firstConsumerPulls));
        Assert.Equal(2, created.Count);
    }

    [Fact]
    public async Task Ordered_consume_leaves_a_503_to_the_heartbeat_timeout_once_the_consumer_has_replied()
    {
        var created = new ConcurrentQueue<string>();
        var pulls = 0;
        await using var ms = new MockServer((_, cmd) =>
        {
            if (TryHandleConsumerLifecycle(cmd, created) || !IsPull(cmd))
                return Task.CompletedTask;

            // A message for the first pull, which takes pending to the threshold so the consumer pulls again
            if (Interlocked.Increment(ref pulls) == 1)
                SendJsMsg(cmd);
            else
                cmd.Reply(headers: "NATS/1.0 503");

            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        await nats.ConnectRetryAsync();
        var consumer = await nats.CreateJetStreamContext().CreateOrderedConsumerAsync("x");

        using var cts = new CancellationTokenSource(QuickRepullsRunOut);
        var received = 0;
        try
        {
            await foreach (var unused in consumer.ConsumeAsync<string>(opts: new NatsJSConsumeOpts { MaxMsgs = 2, IdleHeartbeat = LongIdleHeartbeat }, cancellationToken: cts.Token))
                received++;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(1, received);
        Assert.Equal(2, Volatile.Read(ref pulls));
        Assert.Single(created);
    }

    private static bool IsPull(MockServer.Cmd cmd) => cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.MSG.NEXT");

    // $JS.API.CONSUMER.MSG.NEXT.<stream>.<consumer>
    private static string ConsumerOf(MockServer.Cmd cmd) => cmd.Subject.Split('.')[^1];

    private static bool TryHandleConsumerInfo(MockServer.Cmd cmd)
    {
        if (cmd.Name != "PUB" || !cmd.Subject.Contains("CONSUMER.INFO"))
            return false;

        cmd.Reply(payload: """{"stream_name":"x","name":"c"}""");
        return true;
    }

    private static bool TryHandleConsumerLifecycle(MockServer.Cmd cmd, ConcurrentQueue<string> created)
    {
        if (cmd.Name != "PUB")
            return false;

        if (cmd.Subject.Contains("CONSUMER.CREATE"))
        {
            // $JS.API.CONSUMER.CREATE.<stream>.<consumer>
            var name = cmd.Subject.Split('.')[5];
            created.Enqueue(name);
            cmd.Reply(payload: $$$"""{"stream_name":"x","name":"{{{name}}}","config":{"ack_policy":"none"}}""");
            return true;
        }

        if (cmd.Subject.Contains("CONSUMER.DELETE"))
        {
            cmd.Reply(payload: """{"success":true}""");
            return true;
        }

        return false;
    }

    // Delivered as JetStream does: with the stream message's subject, on the subscription of the pull request's inbox
    private static void SendJsMsg(MockServer.Cmd cmd)
    {
        var sid = cmd.Client.GetSid(cmd.ReplyTo!);
        var ackSubject = $"$JS.ACK.x.{ConsumerOf(cmd)}.1.1.1.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000}.0";
        cmd.Client.Writer.Write($"MSG x.foo {sid} {ackSubject} 1\r\n1\r\n");
        cmd.Client.Writer.Flush();
    }
}
