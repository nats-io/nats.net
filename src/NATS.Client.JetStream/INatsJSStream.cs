using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public interface INatsJSStream
{
    /// <summary>
    /// Stream info object as retrieved from NATS JetStream server at the time this object was created, updated or refreshed.
    /// </summary>
    StreamInfo Info { get; }

    /// <summary>
    /// Delete this stream.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>After deletion this object can't be used anymore.</remarks>
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge data from this stream. Leaves the stream.
    /// </summary>
    /// <param name="request">Purge request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>After deletion this object can't be used anymore.</remarks>
    ValueTask<StreamPurgeResponse> PurgeAsync(StreamPurgeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message from a stream.
    /// </summary>
    /// <param name="request">Delete message request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Delete message response</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<StreamMsgDeleteResponse> DeleteMessageAsync(StreamMsgDeleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update stream properties on the server.
    /// </summary>
    /// <param name="request">Stream update request to be sent to the server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask UpdateAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates new consumer for this stream if it doesn't exists or returns an existing one with the same name.
    /// </summary>
    /// <param name="consumer">Name of the consumer.</param>
    /// <param name="ackPolicy">Ack policy to use. Must not be set to <c>none</c>. Default is <c>explicit</c>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsJSConsumer> CreateConsumerAsync(string consumer, ConsumerConfigurationAckPolicy ackPolicy = ConsumerConfigurationAckPolicy.@explicit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates new consumer for this stream if it doesn't exists or returns an existing one with the same name.
    /// </summary>
    /// <param name="request">Consumer creation request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsJSConsumer> CreateConsumerAsync(ConsumerCreateRequest request, CancellationToken cancellationToken = default);

    ValueTask<INatsJSConsumer> CreateOrderedConsumerAsync(NatsJSOrderedConsumerOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets consumer information from the server and creates a NATS JetStream consumer <see cref="NatsJSConsumer"/>.
    /// </summary>
    /// <param name="consumer">Consumer name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsJSConsumer> GetConsumerAsync(string consumer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates through consumers belonging to this stream.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of consumer objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>
    /// Note that paging isn't implemented. You might receive only a partial list of consumers if there are a lot of them.
    /// </remarks>
    IAsyncEnumerable<ConsumerInfo> ListConsumersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a consumer from this stream.
    /// </summary>
    /// <param name="consumer">Consumer name to be deleted.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether the deletion was successful.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<bool> DeleteConsumerAsync(string consumer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the stream info from the server and update this stream.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

    ValueTask<NatsMsg<T>> GetDirectAsync<T>(StreamMsgGetRequest request, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default);

    ValueTask<StreamMsgGetResponse> GetAsync(StreamMsgGetRequest request, CancellationToken cancellationToken = default);
}
