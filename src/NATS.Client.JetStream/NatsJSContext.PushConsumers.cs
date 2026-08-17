using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext : INatsJSContext
{
    /// <inheritdoc />
    public ValueTask<INatsJSPushConsumer> CreatePushConsumerAsync(
        string stream,
        NatsJSPushConsumerOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        opts ??= NatsJSPushConsumerOpts.Default;
        return CreatePushConsumerInternalAsync(stream, opts, ConsumerCreateAction.Create, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<INatsJSPushConsumer> CreateOrUpdatePushConsumerAsync(
        string stream,
        NatsJSPushConsumerOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        opts ??= NatsJSPushConsumerOpts.Default;
        return CreatePushConsumerInternalAsync(stream, opts, ConsumerCreateAction.CreateOrUpdate, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<INatsJSPushConsumer> GetPushConsumerAsync(
        string stream,
        string consumer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<object, ConsumerInfo>(
            subject: $"{Opts.Prefix}.CONSUMER.INFO.{stream}.{consumer}",
            request: null,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Config.DeliverSubject))
        {
            throw new NatsJSException($"Consumer '{stream}:{consumer}' doesn't have a deliver subject so it can't be consuming as a push consumer.");
        }

        return new NatsJSPushConsumer(this, response);
    }

    /// <inheritdoc />
    public ValueTask<INatsJSPushConsumer> CreateOrderedPushConsumerAsync(
        string stream,
        NatsJSOrderedConsumerOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        opts ??= NatsJSOrderedConsumerOpts.Default;
        return new ValueTask<INatsJSPushConsumer>(new NatsJSOrderedPushConsumer(stream, this, opts, cancellationToken));
    }

    private async ValueTask<INatsJSPushConsumer> CreatePushConsumerInternalAsync(
        string stream,
        NatsJSPushConsumerOpts opts,
        ConsumerCreateAction action,
        CancellationToken cancellationToken)
    {
        var deliverSubject = opts.DeliverSubject ?? NewBaseInbox();

        if (string.IsNullOrWhiteSpace(deliverSubject))
        {
            throw new NatsJSException("A push consumer requires a non-empty deliver subject.");
        }

        var config = new ConsumerConfig
        {
            Name = opts.Name,
            DurableName = opts.DurableName,
            Description = opts.Description,
            FilterSubject = opts.FilterSubject,
            DeliverPolicy = opts.DeliverPolicy,
            AckPolicy = opts.AckPolicy,
            FlowControl = opts.FlowControl,
            HeadersOnly = opts.HeadersOnly,
            DeliverSubject = deliverSubject,
            DeliverGroup = opts.DeliverGroup,
        };

        if (opts.FilterSubjects is { Count: > 0 } filterSubjects)
        {
            config.FilterSubjects = filterSubjects;
        }

        if (opts.OptStartSeq > 0)
        {
            config.OptStartSeq = opts.OptStartSeq;
        }

        if (opts.OptStartTime != default)
        {
            config.OptStartTime = opts.OptStartTime;
        }

        if (opts.AckWait != TimeSpan.Zero)
        {
            config.AckWait = opts.AckWait;
        }

        if (opts.MaxDeliver > 0)
        {
            config.MaxDeliver = opts.MaxDeliver;
        }

        if (opts.MaxAckPending > 0)
        {
            config.MaxAckPending = opts.MaxAckPending;
        }

        if (opts.InactiveThreshold is { } inactiveThreshold)
        {
            config.InactiveThreshold = inactiveThreshold;
        }

        if (opts.IdleHeartbeat is { } idleHeartbeat)
        {
            config.IdleHeartbeat = idleHeartbeat;
        }

        var consumer = await CreateOrUpdateConsumerInternalAsync(stream, config, action, cancellationToken);
        var subOpts = opts.SubOpts ?? new NatsSubOpts { ChannelOpts = new NatsSubChannelOpts { Capacity = 1_000 } };

        return new NatsJSPushConsumer(this, consumer.Info, subOpts, opts.NotificationHandler);
    }
}
