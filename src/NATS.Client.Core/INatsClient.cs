namespace NATS.Client.Core;

public interface INatsClient : IAsyncDisposable
{
    /// <summary>
    /// Represents a connection to the NATS server.
    /// </summary>
    INatsConnection Connection { get; }

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    ValueTask ConnectAsync();

    /// <summary>
    /// Send PING command and await PONG. Return value is similar as Round Trip Time (RTT).
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous round trip operation.</returns>
    ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a serializable message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="data">Serializable data object.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync<T>(string subject, T data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an empty message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Publishing a sentinel usually means a signal to the given subject which could be used to trigger an action
    /// or indicate an event for example and of messages.
    /// </remarks>
    ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a subscription to a subject, optionally joining a distributed queue group.
    /// </summary>
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="queueGroup">If specified, the subscriber will join this queue group.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsSubOpts"/> for subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be received from the NATS Server.</typeparam>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg{T}"/> objects</returns>
    /// <remarks>
    /// Subscribers with the same queue group name, become a queue group,
    /// and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </remarks>
    IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request and receive a single reply from a responder.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="data">Data to send to responder</param>
    /// <param name="headers">Optional message headers</param>
    /// <param name="requestSerializer">Serializer to use for the request message type.</param>
    /// <param name="replySerializer">Serializer to use for the reply message type.</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>Returns the <see cref="NatsMsg{T}"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg{T}"/>.
    /// Reply option's max messages will be set to 1.
    /// If reply option's timeout is not defined, then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an empty request message and await the reply message asynchronously.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="replySerializer">Serializer to use for the reply message type.</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>Returns the <see cref="NatsMsg{T}"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg{T}"/>.
    /// Reply option's max messages will be set to 1.
    /// If reply option's timeout is not defined, then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    ValueTask<NatsMsg<TReply>> RequestAsync<TReply>(
        string subject,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);
}
