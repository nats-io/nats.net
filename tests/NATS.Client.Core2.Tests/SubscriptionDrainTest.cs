using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

[Collection("nats-server")]
public class SubscriptionDrainTest
{
    private readonly NatsServerFixture _server;

    public SubscriptionDrainTest(NatsServerFixture server)
    {
        _server = server;
    }

    [Fact]
    public async Task Drain_preserves_buffered_messages_and_completes_channel()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var subject = $"foo.{Guid.NewGuid():N}";
        var sub = await nats.SubscribeCoreAsync<int>(subject, cancellationToken: cancellationToken);

        const int count = 10;
        for (var i = 0; i < count; i++)
            await nats.PublishAsync(subject, i, cancellationToken: cancellationToken);

        // PING/PONG round-trip guarantees the published messages have been
        // delivered back to our subscription channel before we drain.
        await nats.PingAsync(cancellationToken);

        await sub.DrainAsync(cancellationToken);

        // All buffered messages are still readable and the channel completes.
        var received = new List<int>();
        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
            received.Add(msg.Data);

        Assert.Equal(Enumerable.Range(0, count), received);
    }

    [Fact]
    public async Task Drain_delivers_messages_still_in_flight_after_unsub()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var subject = $"foo.{Guid.NewGuid():N}";
        var sub = await nats.SubscribeCoreAsync<int>(subject, cancellationToken: cancellationToken);

        // Kept below the default SubPendingChannelCapacity so nothing is dropped for being
        // over capacity; what is under test is the drain fence, not back-pressure.
        const int count = 1000;
        for (var i = 0; i < count; i++)
            await nats.PublishAsync(subject, i, cancellationToken: cancellationToken);

        // Unlike the test above there is no PING/PONG before draining, so messages the
        // server sent before it processed our UNSUB are still in flight (not yet in the
        // channel). The drain fences inbound delivery with its own PING/PONG so they all
        // land before the channel completes; without that fence the tail would be dropped.
        await sub.DrainAsync(cancellationToken);

        var received = new List<int>();
        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
            received.Add(msg.Data);

        // Check the count first so a dropped message surfaces as a count mismatch rather
        // than a confusing order-equality failure.
        Assert.Equal(count, received.Count);
        Assert.Equal(Enumerable.Range(0, count), received);
    }

    [Fact]
    public async Task Drain_stops_new_messages_but_connection_stays_usable()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var subject = $"foo.{Guid.NewGuid():N}";
        var sub = await nats.SubscribeCoreAsync<int>(subject, cancellationToken: cancellationToken);

        await nats.PublishAsync(subject, 1, cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);

        await sub.DrainAsync(cancellationToken);

        // Published after the UNSUB; must not reach the drained subscription.
        await nats.PublishAsync(subject, 2, cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);

        var received = new List<int>();
        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
            received.Add(msg.Data);

        Assert.Equal(new[] { 1 }, received);

        // The connection is still usable for a fresh subscription and publish.
        var sub2 = await nats.SubscribeCoreAsync<int>(subject, cancellationToken: cancellationToken);
        await nats.PublishAsync(subject, 42, cancellationToken: cancellationToken);
        var next = await sub2.Msgs.ReadAsync(cancellationToken);
        Assert.Equal(42, next.Data);
        await sub2.DisposeAsync();
    }

    [Fact]
    public async Task Drain_is_idempotent_and_dispose_after_drain_is_safe()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var subject = $"foo.{Guid.NewGuid():N}";
        var sub = await nats.SubscribeCoreAsync<int>(subject, cancellationToken: cancellationToken);

        await sub.DrainAsync(cancellationToken);
        await sub.DrainAsync(cancellationToken);
        await sub.DisposeAsync();
    }
}
