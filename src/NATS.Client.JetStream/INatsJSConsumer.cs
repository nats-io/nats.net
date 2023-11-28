using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public interface INatsJSConsumer
{
    /// <summary>
    /// Consumer info object as retrieved from NATS JetStream server at the time this object was created, updated or refreshed.
    /// </summary>
    ConsumerInfo Info { get; }

    /// <summary>
    /// Starts an enumerator consuming messages from the stream using this consumer.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Consume options. (default: <c>MaxMsgs</c> 1,000)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    IAsyncEnumerable<NatsJSMsg<T>> ConsumeAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSConsumeOpts? opts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consume a single message from the stream using this consumer.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Next message options. (default: 30 seconds timeout)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Message retrieved from the stream or <c>NULL</c></returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <remarks>
    /// <para>
    /// If the request to server expires (in 30 seconds by default) this call returns <c>NULL</c>.
    /// </para>
    /// <para>
    /// This method is implemented as a fetch with <c>MaxMsgs=1</c> which means every request will create a new subscription
    /// on the NATS server. This would be inefficient if you're consuming a lot of messages and you should consider using
    /// fetch or consume methods.
    /// </para>
    /// </remarks>
    /// <example>
    /// The following example shows how you might process messages:
    /// <code lang="C#">
    /// var next = await consumer.NextAsync&lt;Data&gt;();
    /// if (next is { } msg)
    /// {
    ///     // process the message
    ///     await msg.AckAsync();
    /// }
    /// </code>
    /// </example>
    ValueTask<NatsJSMsg<T>?> NextAsync<T>(INatsDeserialize<T>? serializer = default, NatsJSNextOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consume a set number of messages from the stream using this consumer.
    /// </summary>
    /// <param name="opts">Fetch options. (default: <c>MaxMsgs</c> 1,000 and timeout in 30 seconds)</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    IAsyncEnumerable<NatsJSMsg<T>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consume a set number of messages from the stream using this consumer.
    /// </summary>
    /// <param name="maxMsgs">Maximum number of messages to return.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the call.</param>
    /// <typeparam name="T">Message type to deserialize.</typeparam>
    /// <returns>Async enumerable of messages which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">Consumer is deleted, it's push based or request sent to server is invalid.</exception>
    /// <exception cref="NatsJSException">There is an error sending the message or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    IAsyncEnumerable<NatsJSMsg<T>> FetchAsync<T>(
        int maxMsgs,
        INatsDeserialize<T>? serializer = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the consumer info from the server and update this consumer.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}
