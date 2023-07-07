using System.Buffers;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsRequestManyExtensions
{
    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="payload">Payload to send to responder</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static async IAsyncEnumerable<NatsMsg> RequestManyAsync(
        this NatsConnection nats,
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (replyOpts == null || !replyOpts.Value.CanBeCancelled.HasValue)
        {
            replyOpts = (replyOpts ?? default) with { CanBeCancelled = true, };
        }

        await using var sub = await nats.RequestSubAsync(subject, payload, requestOpts, replyOpts, cancellationToken).ConfigureAwait(false);

        while (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            while (sub.Msgs.TryRead(out var msg))
            {
                // Received end of stream sentinel
                if (msg.Data.Length == 0)
                {
                    yield break;
                }

                yield return msg;
            }
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Inbox subscription cancelled");
        }
    }

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
        this NatsConnection nats,
        NatsMsg msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestManyAsync(
            nats,
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
    /// <param name="subject">Subject of the responder</param>
    /// <param name="data">Data to send to responder</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static async IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        this NatsConnection nats,
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (replyOpts == null || !replyOpts.Value.CanBeCancelled.HasValue)
        {
            replyOpts = (replyOpts ?? default) with { CanBeCancelled = true, };
        }

        await using var sub = await nats.RequestSubAsync<TRequest, TReply>(subject, data, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        while (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            while (sub.Msgs.TryRead(out var msg))
            {
                // Received end of stream sentinel
                if (msg.Data is null)
                {
                    yield break;
                }

                yield return msg;
            }
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Inbox subscription cancelled");
        }
    }

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
        this NatsConnection nats,
        NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestManyAsync<TRequest, TReply>(
            nats,
            msg.Subject,
            msg.Data,
            requestOpts: new NatsPubOpts
            {
                Headers = msg.Headers,
            },
            replyOpts,
            cancellationToken);
}
