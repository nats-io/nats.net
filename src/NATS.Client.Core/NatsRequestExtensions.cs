using System.Buffers;

namespace NATS.Client.Core;

public static class NatsRequestExtensions
{
    /// <summary>
    /// Create a new inbox subject with the form {Inbox Prefix}.{Unique Connection ID}.{Unique Inbox ID}
    /// </summary>
    /// <returns>A <see cref="string"/> containing a unique inbox subject.</returns>
    public static string NewInbox(this NatsConnection nats) => $"{nats.InboxPrefix}{Guid.NewGuid():n}";

    /// <summary>
    /// Request and receive a single reply from a responder.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="msg">Message to be sent as request</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>Returns the <see cref="NatsMsg{T}"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg"/>.
    /// Reply option's max messages will be set to 1.
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        this INatsConnection nats,
        in NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        nats.RequestAsync<TRequest, TReply>(
            msg.Subject,
            msg.Data,
            requestOpts: new NatsPubOpts
            {
                Headers = msg.Headers,
            },
            replyOpts,
            cancellationToken);

    /// <summary>
    /// Request and receive a single reply from a responder.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="msg">Message to be sent as request</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <returns>Returns the <see cref="NatsMsg"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg"/>.
    /// Reply option's max messages will be set to 1.
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static ValueTask<NatsMsg?> RequestAsync(
        this INatsConnection nats,
        in NatsMsg msg,
        in NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        nats.RequestAsync(
            msg.Subject,
            payload: new ReadOnlySequence<byte>(msg.Data),
            requestOpts: new NatsPubOpts
            {
                Headers = msg.Headers,
            },
            replyOpts,
            cancellationToken);
}
