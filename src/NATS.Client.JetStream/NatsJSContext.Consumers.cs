using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
    /// <summary>
    /// Creates new ordered consumer.
    /// </summary>
    /// <param name="stream">Stream name to create the consumer under.</param>
    /// <param name="opts">Ordered consumer options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving ordered data from the stream.</returns>
    public ValueTask<NatsJSOrderedConsumer> CreateOrderedConsumerAsync(
        string stream,
        NatsJSOrderedConsumerOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        opts ??= NatsJSOrderedConsumerOpts.Default;
        return new ValueTask<NatsJSOrderedConsumer>(new NatsJSOrderedConsumer(stream, this, opts, cancellationToken));
    }

    /// <summary>
    /// Creates new consumer if it doesn't exists or returns an existing one with the same name.
    /// </summary>
    /// <param name="stream">Stream name to create the consumer under.</param>
    /// <param name="consumer">Name of the consumer.</param>
    /// <param name="ackPolicy">Ack policy to use. Must not be set to <c>none</c>. Default is <c>explicit</c>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<NatsJSConsumer> CreateConsumerAsync(
        string stream,
        string consumer,
        ConsumerConfigurationAckPolicy ackPolicy = ConsumerConfigurationAckPolicy.@explicit,
        CancellationToken cancellationToken = default) =>
        CreateConsumerAsync(
            new ConsumerCreateRequest
            {
                StreamName = stream,
                Config = new ConsumerConfiguration
                {
                    Name = consumer,
                    DurableName = consumer,
                    AckPolicy = ackPolicy,
                },
            },
            cancellationToken);

    /// <summary>
    /// Creates new consumer if it doesn't exists or returns an existing one with the same name.
    /// </summary>
    /// <param name="request">Consumer creation request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask<NatsJSConsumer> CreateConsumerAsync(
        ConsumerCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Adjust API subject according to server version and filter subject
        var subject = $"{Opts.Prefix}.CONSUMER.CREATE.{request.StreamName}";

        if (!string.IsNullOrWhiteSpace(request.Config.Name))
        {
            subject += $".{request.Config.Name}";
            request.Config.Name = default!;
        }

        if (!string.IsNullOrWhiteSpace(request.Config.FilterSubject))
        {
            subject += $".{request.Config.FilterSubject}";
        }

        var response = await JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            subject: subject,
            request,
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
    public async ValueTask<NatsJSConsumer> GetConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, ConsumerInfo>(
            subject: $"{Opts.Prefix}.CONSUMER.INFO.{stream}.{consumer}",
            request: null,
            cancellationToken);
        return new NatsJSConsumer(this, response);
    }

    /// <summary>
    /// Enumerates through consumers belonging to a stream.
    /// </summary>
    /// <param name="stream">Stream name the consumers belong to.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of consumer info objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>
    /// Note that paging isn't implemented. You might receive only a partial list of consumers if there are a lot of them.
    /// </remarks>
    public async IAsyncEnumerable<ConsumerInfo> ListConsumersAsync(
        string stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<ConsumerListRequest, ConsumerListResponse>(
            subject: $"{Opts.Prefix}.CONSUMER.LIST.{stream}",
            new ConsumerListRequest { Offset = 0 },
            cancellationToken);
        foreach (var consumer in response.Consumers)
            yield return consumer;
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
    public async ValueTask<bool> DeleteConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, ConsumerDeleteResponse>(
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
            Config = new ConsumerConfiguration
            {
                DeliverPolicy = opts.DeliverPolicy,
                AckPolicy = ConsumerConfigurationAckPolicy.none,
                ReplayPolicy = opts.ReplayPolicy,
                InactiveThreshold = opts.InactiveThreshold.ToNanos(),
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
            subject: subject,
            request,
            cancellationToken);
    }
}
