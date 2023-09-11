using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
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
        if (!string.IsNullOrEmpty(request.Config.DeliverSubject))
        {
            throw new NatsJSException("This API only support pull consumers. " +
                                      "'deliver_subject' option applies to push consumers");
        }

        if (request.Config.AckPolicy == ConsumerConfigurationAckPolicy.none)
        {
            throw new NatsJSException("This API only support pull consumers. " +
                                      "'ack_policy' must be set to 'explicit' or 'all' for pull consumers");
        }

        var response = await JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            subject: $"{Opts.Prefix}.CONSUMER.CREATE.{request.StreamName}.{request.Config.Name}",
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
    /// <returns>Async enumerable of consumer objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>
    /// Note that paging isn't implemented. You might receive only a partial list of consumers if there are a lot of them.
    /// </remarks>
    public async IAsyncEnumerable<NatsJSConsumer> ListConsumersAsync(
        string stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<ConsumerListRequest, ConsumerListResponse>(
            subject: $"{Opts.Prefix}.CONSUMER.LIST.{stream}",
            new ConsumerListRequest { Offset = 0 },
            cancellationToken);
        foreach (var consumer in response.Consumers)
            yield return new NatsJSConsumer(this, consumer);
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
}
