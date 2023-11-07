namespace NATS.Client.Core;

public static class NatsRequestManyExtensions
{
    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="msg">Message to be sent as request</param>
    /// <param name="requestSerializer">Serializer to use for the request message type.</param>
    /// <param name="replySerializer">Serializer to use for the reply message type.</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg{TReply}"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    public static IAsyncEnumerable<NatsMsg<TReply>> RequestManyAsync<TRequest, TReply>(
        this INatsConnection nats,
        NatsMsg<TRequest> msg,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        NatsRequestExtensions.CheckMsgForRequestReply(msg);

        return nats.RequestManyAsync<TRequest, TReply>(
            msg.Subject,
            msg.Data,
            msg.Headers,
            requestSerializer,
            replySerializer,
            requestOpts,
            replyOpts,
            cancellationToken);
    }
}
