using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext : INatsJSContext
{
    /// <summary>
    /// Creates new ordered consumer.
    /// </summary>
    /// <param name="stream">Stream name to create the consumer under.</param>
    /// <param name="opts">Ordered consumer options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving ordered data from the stream.</returns>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public ValueTask<INatsJSConsumer> CreateOrderedConsumerAsync(
        string stream,
        NatsJSOrderedConsumerOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        opts ??= NatsJSOrderedConsumerOpts.Default;
        return new ValueTask<INatsJSConsumer>(new NatsJSOrderedConsumer(stream, this, opts, cancellationToken));
    }

    /// <inheritdoc />>
    public async ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        return await CreateOrUpdateConsumerInternalAsync(stream, config, default, cancellationToken);
    }

    public async ValueTask<INatsJSConsumer> CreateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        return await CreateOrUpdateConsumerInternalAsync(stream, config, ConsumerCreateAction.Create, cancellationToken);
    }

    public async ValueTask<INatsJSConsumer> UpdateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        return await CreateOrUpdateConsumerInternalAsync(stream, config, ConsumerCreateAction.Update, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<INatsJSConsumer> GetConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<object, ConsumerInfo>(
            subject: $"{Opts.Prefix}.CONSUMER.INFO.{stream}.{consumer}",
            request: null,
            cancellationToken);
        return new NatsJSConsumer(this, response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<INatsJSConsumer> ListConsumersAsync(
        string stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await JSRequestResponseAsync<ConsumerListRequest, ConsumerListResponse>(
                subject: $"{Opts.Prefix}.CONSUMER.LIST.{stream}",
                new ConsumerListRequest { Offset = offset },
                cancellationToken);

            if (response.Consumers.Count == 0)
            {
                yield break;
            }

            foreach (var consumer in response.Consumers)
                yield return new NatsJSConsumer(this, consumer);

            offset += response.Consumers.Count;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListConsumerNamesAsync(
        string stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await JSRequestResponseAsync<ConsumerNamesRequest, ConsumerNamesResponse>(
                subject: $"{Opts.Prefix}.CONSUMER.NAMES.{stream}",
                new ConsumerNamesRequest { Offset = offset },
                cancellationToken);

            if (response.Consumers.Count == 0)
                yield break;

            foreach (var consumer in response.Consumers)
                yield return consumer;

            offset += response.Consumers.Count;
        }
    }

    /// <summary>
    /// Delete a consumer from a stream.
    /// </summary>
    /// <param name="stream">Stream name where consumer is associated to.</param>
    /// <param name="consumer">Consumer name to be deleted.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether the deletion was successful.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<bool> DeleteConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<object, ConsumerDeleteResponse>(
            subject: $"{Opts.Prefix}.CONSUMER.DELETE.{stream}.{consumer}",
            request: null,
            cancellationToken);
        return response.Success;
    }

    /// <summary>
    /// Pause a consumer.
    /// </summary>
    /// <param name="stream">Stream name where consumer is associated to.</param>
    /// <param name="consumer">Consumer name to be paused.</param>
    /// <param name="pauseUntil">Until when the consumer should be paused.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Result of pausing the consumer.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<ConsumerPauseResponse> PauseConsumerAsync(string stream, string consumer, DateTimeOffset pauseUntil, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<ConsumerPauseRequest, ConsumerPauseResponse>(
            subject: $"{Opts.Prefix}.CONSUMER.PAUSE.{stream}.{consumer}",
            request: new ConsumerPauseRequest { PauseUntil = pauseUntil },
            cancellationToken);
        return response;
    }

    /// <summary>
    /// Resume a (paused) consumer.
    /// </summary>
    /// <param name="stream">Stream name where consumer is associated to.</param>
    /// <param name="consumer">Consumer name to be resumed.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Result of resuming the (paused) consumer.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<bool> ResumeConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<object, ConsumerPauseResponse>(
            subject: $"{Opts.Prefix}.CONSUMER.PAUSE.{stream}.{consumer}",
            request: null,
            cancellationToken);
        return !response.IsPaused;
    }

    /// <inheritdoc />
    public async ValueTask UnpinConsumerAsync(string stream, string consumer, string group, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        await JSRequestResponseAsync<ConsumerUnpinRequest, ConsumerUnpinResponse>(
            subject: $"{Opts.Prefix}.CONSUMER.UNPIN.{stream}.{consumer}",
            request: new ConsumerUnpinRequest { Group = group },
            cancellationToken);
    }

    internal ValueTask<ConsumerInfo> CreateOrderedConsumerInternalAsync(
        string stream,
        NatsJSOrderedConsumerOpts opts,
        CancellationToken cancellationToken)
    {
        var request = new ConsumerCreateRequest
        {
            StreamName = stream,
            Config = new ConsumerConfig
            {
                DeliverPolicy = opts.DeliverPolicy,
                AckPolicy = ConsumerConfigAckPolicy.None,
                ReplayPolicy = opts.ReplayPolicy,
                InactiveThreshold = opts.InactiveThreshold,
                NumReplicas = 1,
                MemStorage = true,
            },
            Action = ConsumerCreateAction.Create,
        };

        if (opts.OptStartSeq > 0)
        {
            request.Config.OptStartSeq = opts.OptStartSeq;
        }

        if (opts.OptStartTime != default)
        {
            request.Config.OptStartTime = opts.OptStartTime;
        }

        if (opts.HeadersOnly)
        {
            request.Config.HeadersOnly = true;
        }

        if (opts.FilterSubjects.Length > 0)
        {
            request.Config.FilterSubjects = opts.FilterSubjects;
        }

        var name = Nuid.NewNuid();
        var subject = $"{Opts.Prefix}.CONSUMER.CREATE.{stream}.{name}";

        return JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            subject: subject,
            request,
            cancellationToken);
    }

    private async ValueTask<NatsJSConsumer> CreateOrUpdateConsumerInternalAsync(
        string stream,
        ConsumerConfig config,
        ConsumerCreateAction action,
        CancellationToken cancellationToken)
    {
        var subject = $"{Opts.Prefix}.CONSUMER.CREATE.{stream}";

        if (!string.IsNullOrWhiteSpace(config.Name))
        {
            subject += $".{config.Name}";
        }

        if (!string.IsNullOrWhiteSpace(config.FilterSubject))
        {
            subject += $".{config.FilterSubject}";
        }

        // ADR-42: In the initial implementation we should limit PriorityGroups to one per consumer only
        // and error should one be made with multiple groups. In future iterations multiple groups will
        // be supported along with dynamic partitioning of stream data.
        if (config.PriorityGroups != null && config.PriorityGroups.Count != 1)
        {
            throw new NatsJSException("Cannot create consumers with multiple priority groups.");
        }

        var response = await JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            subject: subject,
            new ConsumerCreateRequest
            {
                StreamName = stream,
                Config = config,
                Action = action,
            },
            cancellationToken);

        return new NatsJSConsumer(this, response);
    }
}
