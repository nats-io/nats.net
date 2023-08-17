using System.Buffers;

namespace NATS.Client.Core;

public static class NatsRequestManyExtensions
{
    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="msg">Message to be sent as request</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static IAsyncEnumerable<NatsMsg> RequestManyAsync(
        this INatsConnection nats,
        NatsMsg msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        nats.RequestManyAsync(
            msg.Subject,
            payload: new ReadOnlySequence<byte>(msg.Data),
            requestOpts: new NatsPubOpts
            {
                Headers = msg.Headers,
            },
            replyOpts,
            cancellationToken);

    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="msg">Message to be sent as request</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        this INatsConnection nats,
        NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        nats.RequestManyAsync<TRequest, TReply>(
            msg.Subject,
            msg.Data,
            requestOpts: new NatsPubOpts
            {
                Headers = msg.Headers,
            },
            replyOpts,
            cancellationToken);
}
