using System.Buffers;

namespace NATS.Client.Core;

public interface INatsConnection
{
    /// <summary>
    /// Send PING command and await PONG. Return value is similar as Round Trip Time (RTT).
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous round trip operation.</returns>
    ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="payload">The message payload data.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync(in NatsMsg msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a serializable message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="data">Serializable data object.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync<T>(string subject, T data, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a serializable message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg{T}"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync<T>(in NatsMsg<T> msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a subscription to a subject, optionally joining a distributed queue group.
    /// </summary>
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="queueGroup">If specified, the subscriber will join this queue group.</param>
    /// <param name="opts">A <see cref="NatsSubOpts"/> for subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Subscribers with the same queue group name, become a queue group,
    /// and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </remarks>
    ValueTask<INatsSub> SubscribeAsync(string subject, string? queueGroup = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a subscription to a subject, optionally joining a distributed queue group.
    /// </summary>
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="queueGroup">If specified, the subscriber will join this queue group.</param>
    /// <param name="opts">A <see cref="NatsSubOpts"/> for subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be received from the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous send operation.</returns>
    /// <remarks>
    /// Subscribers with the same queue group name, become a queue group,
    /// and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </remarks>
    ValueTask<INatsSub<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new inbox subject with the form {Inbox Prefix}.{Unique Connection ID}.{Unique Inbox ID}
    /// </summary>
    /// <returns>A <see cref="string"/> containing a unique inbox subject.</returns>
    string NewInbox();

    /// <summary>
    /// Request and receive a single reply from a responder.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="data">Data to send to responder</param>
    /// <param name="headers">Optional message headers</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>Returns the <see cref="NatsMsg{T}"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg"/>.
    /// Reply option's max messages will be set to 1.
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Request and receive a single reply from a responder.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="payload">Payload to send to responder</param>
    /// <param name="headers">Optional message headers</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <returns>Returns the <see cref="NatsMsg"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg"/>.
    /// Reply option's max messages will be set to 1 (one).
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    ValueTask<NatsMsg?> RequestAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="data">Data to send to responder</param>
    /// <param name="headers">Optional message headers</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="payload">Payload to send to responder</param>
    /// <param name="headers">Optional message headers</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    IAsyncEnumerable<NatsMsg> RequestManyAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);
}
