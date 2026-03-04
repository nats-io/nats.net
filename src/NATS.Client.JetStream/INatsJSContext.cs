using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public interface INatsJSContext
{
    /// <summary>
    /// Connection to the NATS server.
    /// </summary>
    INatsConnection Connection { get; }

    /// <summary>
    /// Provides configuration options for the JetStream context.
    /// </summary>
    NatsJSOpts Opts { get; }

    /// <summary>
    /// Creates new ordered consumer.
    /// </summary>
    /// <param name="stream">Stream name to create the consumer under.</param>
    /// <param name="opts">Ordered consumer options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving ordered data from the stream.</returns>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    ValueTask<INatsJSConsumer> CreateOrderedConsumerAsync(
        string stream,
        NatsJSOrderedConsumerOpts? opts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates new consumer if it doesn't exists or updates an existing one with the same name.
    /// </summary>
    /// <param name="stream">Name of the stream to create or update consumer under.</param>
    /// <param name="config">Consumer configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates new consumer if it doesn't exists.
    /// </summary>
    /// <param name="stream">Name of the stream to create consumer under.</param>
    /// <param name="config">Consumer configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    ValueTask<INatsJSConsumer> CreateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update consumer exists consumer
    /// </summary>
    /// <param name="stream">Name of the stream to update consumer under.</param>
    /// <param name="config">Consumer configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    ValueTask<INatsJSConsumer> UpdateConsumerAsync(
        string stream,
        ConsumerConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets consumer information from the server and creates a NATS JetStream consumer <see cref="NatsJSConsumer"/>.
    /// </summary>
    /// <param name="stream">Stream name where consumer is associated to.</param>
    /// <param name="consumer">Consumer name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Warning:</b> This method issues a <c>$JS.API.CONSUMER.INFO</c> request to the server on every call.
    /// Calling it frequently (e.g., in a message-processing loop or on a short timer) can cause significant load
    /// on the NATS cluster, lead to API timeouts, and degrade performance for all clients.
    /// </para>
    /// <para>
    /// If you need to track consumer progress at runtime (e.g., pending message count, sequence numbers, or delivery attempts),
    /// use <see cref="INatsJSMsg{T}.Metadata"/> on each received message instead. When available, it provides
    /// <see cref="NatsJSMsgMetadata.NumPending"/>, <see cref="NatsJSMsgMetadata.NumDelivered"/>,
    /// <see cref="NatsJSMsgMetadata.Sequence"/>, and <see cref="NatsJSMsgMetadata.Timestamp"/>
    /// without requiring a server round-trip. Note that <see cref="INatsJSMsg{T}.Metadata"/> can be <c>null</c>
    /// (for example, if the reply subject cannot be parsed), so callers should always check for <c>null</c> before
    /// accessing its properties.
    /// </para>
    /// <para>
    /// Prefer using <see cref="CreateOrUpdateConsumerAsync"/> or <see cref="CreateConsumerAsync"/> to obtain a consumer
    /// handle. Reserve this method for cases where you need to retrieve a consumer that was already created separately.
    /// </para>
    /// </remarks>
    ValueTask<INatsJSConsumer> GetConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates through consumers belonging to a stream.
    /// </summary>
    /// <param name="stream">Stream name the consumers belong to.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of consumer info objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    IAsyncEnumerable<INatsJSConsumer> ListConsumersAsync(
        string stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates through consumer names belonging to a stream.
    /// </summary>
    /// <param name="stream">Stream name the consumers belong to.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of consumer info objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    IAsyncEnumerable<string> ListConsumerNamesAsync(
        string stream,
        CancellationToken cancellationToken = default);

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
    ValueTask<bool> DeleteConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default);

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
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    ValueTask<ConsumerPauseResponse> PauseConsumerAsync(string stream, string consumer, DateTimeOffset pauseUntil, CancellationToken cancellationToken = default);

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
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    ValueTask<bool> ResumeConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unpin a consumer from the currently pinned client.
    /// </summary>
    /// <param name="stream">Stream name where consumer is associated to.</param>
    /// <param name="consumer">Consumer name to be unpinned.</param>
    /// <param name="group">The priority group name to unpin.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    ValueTask UnpinConsumerAsync(string stream, string consumer, string group, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls JetStream Account Info API.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The account information based on the NATS connection credentials.</returns>
    ValueTask<AccountInfoResponse> GetAccountInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends data to a stream associated with the subject.
    /// </summary>
    /// <param name="subject">Subject to publish the data to.</param>
    /// <param name="data">Data to publish.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Publish options.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the publishing call or the wait for response.</param>
    /// <typeparam name="T">Type of the data being sent.</typeparam>
    /// <returns>
    /// The ACK response to indicate if stream accepted the message as well as additional
    /// information like the sequence number of the message stored by the stream.
    /// </returns>
    /// <exception cref="NatsJSException">There was a problem receiving the response.</exception>
    /// <remarks>
    /// <para>
    /// Note that if the subject isn't backed by a stream or the connected NATS server
    /// isn't running with JetStream enabled, this call will hang waiting for an ACK
    /// until the request times out.
    /// </para>
    /// </remarks>
    ValueTask<PubAckResponse> PublishAsync<T>(
        string subject,
        T? data,
        INatsSerialize<T>? serializer = default,
        NatsJSPubOpts? opts = default,
        NatsHeaders? headers = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to send data to a stream associated with the subject.
    /// </summary>
    /// <param name="subject">Subject to publish the data to.</param>
    /// <param name="data">Data to publish.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Publish options.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the publishing call or the wait for response.</param>
    /// <typeparam name="T">Type of the data being sent.</typeparam>
    /// <returns>
    /// The ACK response to indicate if stream accepted the message as well as additional
    /// information like the sequence number of the message stored by the stream.
    /// </returns>
    /// <exception cref="NatsJSException">There was a problem receiving the response.</exception>
    /// <remarks>
    /// <para>
    /// Use this method to avoid exceptions.
    /// </para>
    /// <para>
    /// Note that if the subject isn't backed by a stream or the connected NATS server
    /// isn't running with JetStream enabled, this call will hang waiting for an ACK
    /// until the request times out.
    /// </para>
    /// </remarks>
    ValueTask<NatsResult<PubAckResponse>> TryPublishAsync<T>(
        string subject,
        T? data,
        INatsSerialize<T>? serializer = default,
        NatsJSPubOpts? opts = default,
        NatsHeaders? headers = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new stream if it doesn't exist or returns an existing stream with the same name.
    /// </summary>
    /// <param name="config">Stream configuration request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsJSStream> CreateStreamAsync(
        StreamConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new stream if it doesn't exist or update if the stream already exists.
    /// </summary>
    /// <param name="config">Stream configuration request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The stream name in <paramref name="config"/> is invalid.</exception>
    /// <exception cref="ArgumentNullException">The name in <paramref name="config"/> is <c>null</c>.</exception>
    ValueTask<INatsJSStream> CreateOrUpdateStreamAsync(
        StreamConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a stream.
    /// </summary>
    /// <param name="stream">Stream name to be deleted.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<bool> DeleteStreamAsync(
        string stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges all of the (or filtered) data in a stream, leaves the stream.
    /// </summary>
    /// <param name="stream">Stream name to be purged.</param>
    /// <param name="request">Purge request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Purge response</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<StreamPurgeResponse> PurgeStreamAsync(
        string stream,
        StreamPurgeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message from a stream.
    /// </summary>
    /// <param name="stream">Stream name to delete message from.</param>
    /// <param name="request">Delete message request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Delete message response</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<StreamMsgDeleteResponse> DeleteMessageAsync(
        string stream,
        StreamMsgDeleteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get stream information from the server and creates a NATS JetStream stream object <see cref="NatsJSStream"/>.
    /// </summary>
    /// <param name="stream">Name of the stream to retrieve.</param>
    /// <param name="request">Stream info request options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsJSStream> GetStreamAsync(
        string stream,
        StreamInfoRequest? request = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a NATS JetStream stream's properties.
    /// </summary>
    /// <param name="request">Stream update request object to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The updated NATS JetStream stream object.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<NatsJSStream> UpdateStreamAsync(
        StreamConfig request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates through the streams exists on the NATS JetStream server.
    /// </summary>
    /// <param name="subject">Limit the list to streams matching this subject filter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of stream objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    IAsyncEnumerable<INatsJSStream> ListStreamsAsync(
        string? subject = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List stream names.
    /// </summary>
    /// <param name="subject">Limit the list to streams matching this subject filter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable list of stream names to be used in a <c>await foreach</c> loop.</returns>
    IAsyncEnumerable<string> ListStreamNamesAsync(string? subject = default, CancellationToken cancellationToken = default);

    ValueTask<NatsJSPublishConcurrentFuture> PublishConcurrentAsync<T>(
        string subject,
        T? data,
        INatsSerialize<T>? serializer = default,
        NatsJSPubOpts? opts = default,
        NatsHeaders? headers = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new base inbox string using the connection's inbox prefix.
    /// </summary>
    /// <returns>A new inbox string.</returns>
    string NewBaseInbox();

    /// <summary>
    /// Sends a request message to a JetStream subject and waits for a response.
    /// </summary>
    /// <param name="subject">The JetStream API subject to send the request to.</param>
    /// <param name="request">The request message object.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="TRequest">The type of the request message.</typeparam>
    /// <typeparam name="TResponse">The type of the response message.</typeparam>
    /// <returns>A task representing the asynchronous operation, with a result of type <typeparamref name="TResponse"/>.</returns>
    ValueTask<TResponse> JSRequestResponseAsync<TRequest, TResponse>(
        string subject,
        TRequest? request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
}
