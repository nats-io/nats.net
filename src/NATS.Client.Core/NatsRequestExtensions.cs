namespace NATS.Client.Core;

public static class NatsRequestExtensions
{
    /// <summary>
    /// Request and receive a single reply from a responder.
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
    /// <returns>Returns the <see cref="NatsMsg{T}"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg{T}"/>.
    /// Reply option's max messages will be set to 1.
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    public static ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        this INatsConnection nats,
        in NatsMsg<TRequest> msg,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        CheckMsgForRequestReply(msg);

        return nats.RequestAsync<TRequest, TReply>(
            msg.Subject,
            msg.Data,
            msg.Headers,
            requestSerializer,
            replySerializer,
            requestOpts,
            replyOpts,
            cancellationToken);
    }

    internal static void CheckMsgForRequestReply<T>(in NatsMsg<T> msg) => CheckForRequestReply(msg.ReplyTo);

    private static void CheckForRequestReply(string? replyTo)
    {
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            throw new NatsException($"Can't set reply-to for a request");
        }
    }
}
