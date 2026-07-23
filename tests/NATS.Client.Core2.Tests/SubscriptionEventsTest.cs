using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

[Collection("nats-server")]
public class SubscriptionEventsTest
{
    private readonly NatsServerFixture _server;

    public SubscriptionEventsTest(NatsServerFixture server)
    {
        _server = server;
    }

    [Fact]
    public async Task OnSubscribed_fires_when_async_enumerable_subscription_is_established()
    {
        await using var nats1 = new NatsConnection(new NatsOpts { Url = _server.Url });
        await using var nats2 = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats1.ConnectRetryAsync();
        await nats2.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;
        var subject = $"foo.{Guid.NewGuid():N}";

        var subscribed = new TaskCompletionSource<NatsSubBase>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => subscribed.TrySetCanceled(cancellationToken));

        var opts = new NatsSubOpts
        {
            Events = new NatsSubEvents
            {
                OnSubscribed = sub =>
                {
                    subscribed.TrySetResult(sub);
                    return default;
                },
            },
        };

        var received = Task.Run(async () =>
        {
            await foreach (var msg in nats1.SubscribeAsync<int>(subject, opts: opts, cancellationToken: cancellationToken))
                return msg.Data;
            return -1;
        });

        var subscription = await subscribed.Task;
        Assert.Equal(subject, subscription.Subject);

        // A round-trip on the subscribing connection guarantees the server has
        // processed the SUB before the other connection publishes.
        await nats1.PingAsync(cancellationToken);
        await nats2.PublishAsync(subject, 42, cancellationToken: cancellationToken);

        Assert.Equal(42, await received);
    }

    [Fact]
    public async Task OnSubscribed_fires_for_subscribe_core()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;
        var subject = $"foo.{Guid.NewGuid():N}";

        NatsSubBase? subscribed = null;
        var opts = new NatsSubOpts
        {
            Events = new NatsSubEvents
            {
                OnSubscribed = sub =>
                {
                    subscribed = sub;
                    return default;
                },
            },
        };

        await using var sub = await nats.SubscribeCoreAsync<int>(subject, opts: opts, cancellationToken: cancellationToken);

        Assert.Same(sub, subscribed);
    }

    [Fact]
    public async Task OnSubscribed_allows_request_reply_handoff_without_no_responders()
    {
        await using var nats1 = new NatsConnection(new NatsOpts { Url = _server.Url });
        await using var nats2 = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats1.ConnectRetryAsync();
        await nats2.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;
        var subject = $"foo.{Guid.NewGuid():N}";

        var subscribed = new TaskCompletionSource<NatsSubBase>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => subscribed.TrySetCanceled(cancellationToken));

        var opts = new NatsSubOpts
        {
            Events = new NatsSubEvents
            {
                OnSubscribed = sub =>
                {
                    subscribed.TrySetResult(sub);
                    return default;
                },
            },
        };

        // Hand off consuming the subscription to a task, as in the reported
        // scenario, replying to the first request received.
        var responder = Task.Run(async () =>
        {
            await foreach (var msg in nats1.SubscribeAsync<int>(subject, opts: opts, cancellationToken: cancellationToken))
            {
                await msg.ReplyAsync(msg.Data + 1, cancellationToken: cancellationToken);
                break;
            }
        });

        await subscribed.Task;
        await nats1.PingAsync(cancellationToken);

        var reply = await nats2.RequestAsync<int, int>(subject, 1, cancellationToken: cancellationToken);
        Assert.Equal(2, reply.Data);

        await responder;
    }

    [Fact]
    public async Task OnSubscribed_exception_propagates_and_disposes_subscription()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;
        var subject = $"foo.{Guid.NewGuid():N}";

        var opts = new NatsSubOpts
        {
            Events = new NatsSubEvents
            {
                OnSubscribed = _ => throw new InvalidOperationException("callback failed"),
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await nats.SubscribeCoreAsync<int>(subject, opts: opts, cancellationToken: cancellationToken));

        Assert.Equal("callback failed", exception.Message);
    }
}
