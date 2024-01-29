using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
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
    public ValueTask<INatsJSConsumer> CreateOrderedConsumerAsync(
        string stream,
        NatsJSOrderedConsumerOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        opts ??= NatsJSOrderedConsumerOpts.Default;
        return new ValueTask<INatsJSConsumer>(new NatsJSOrderedConsumer(stream, this, opts, cancellationToken));
    }

    /// <inheritdoc />>
    public ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default)
        => CreateOrUpdateConsumerAsync(Telemetry.NatsActivities, stream, config, cancellationToken);

    internal async ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(
        ActivitySource activitySource,
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default)
    {
        // TODO: Adjust API subject according to server version and filter subject
        var subject = $"{Opts.Prefix}.CONSUMER.CREATE.{stream}";

        if (!string.IsNullOrWhiteSpace(config.Name))
        {
            subject += $".{config.Name}";
            config.Name = default!;
        }

        if (!string.IsNullOrWhiteSpace(config.FilterSubject))
        {
            subject += $".{config.FilterSubject}";
        }

        var response = await JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            activitySource,
            subject: subject,
            new ConsumerCreateRequest
            {
                StreamName = stream,
                Config = config,
            },
            cancellationToken);

        return new NatsJSConsumer(this, response);
    }

    /// <summary>
    /// Gets consumer information from the server and creates a NATS JetStream consumer <see cref="NatsJSConsumer"/>.
    /// </summary>
    /// <param name="stream">Stream name where consumer is associated to.</param>
    /// <param name="consumer">Consumer name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1202:Elements should be ordered by access", Justification = "Internal is wrapped by public method.")]
    public async ValueTask<INatsJSConsumer> GetConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, ConsumerInfo>(
            Telemetry.NatsActivities,
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
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await JSRequestResponseAsync<ConsumerListRequest, ConsumerListResponse>(
                Telemetry.NatsActivities,
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
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await JSRequestResponseAsync<ConsumerNamesRequest, ConsumerNamesResponse>(
                Telemetry.NatsActivities,
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
    public ValueTask<bool> DeleteConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
        => DeleteConsumerAsync(Telemetry.NatsActivities, stream, consumer, cancellationToken);

    public async ValueTask<bool> DeleteConsumerAsync(ActivitySource activitySource, string stream, string consumer, CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, ConsumerDeleteResponse>(
            activitySource,
            subject: $"{Opts.Prefix}.CONSUMER.DELETE.{stream}.{consumer}",
            request: null,
            cancellationToken);
        return response.Success;
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

        var name = NuidWriter.NewNuid();
        var subject = $"{Opts.Prefix}.CONSUMER.CREATE.{stream}.{name}";

        return JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            activitySource: Telemetry.NatsInternalActivities,
            subject: subject,
            request,
            cancellationToken);
    }
}
