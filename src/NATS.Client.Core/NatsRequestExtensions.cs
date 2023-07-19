using System.Buffers;
using NATS.Client.Core.Internal;

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
    /// <param name="subject">Subject of the responder</param>
    /// <param name="data">Data to send to responder</param>
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
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static async ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        this NatsConnection nats,
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var opts = nats.SetReplyOptsDefaults(replyOpts);

        await using var sub = await nats.RequestSubAsync<TRequest, TReply>(subject, data, requestOpts, opts, cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Inbox subscription cancelled");
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }

        return null;
    }

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
        this NatsConnection nats,
        in NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestAsync<TRequest, TReply>(
            nats,
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
    /// <param name="subject">Subject of the responder</param>
    /// <param name="payload">Payload to send to responder</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <returns>Returns the <see cref="NatsMsg"/> received from the responder as reply.</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// Response can be (null) or one <see cref="NatsMsg"/>.
    /// Reply option's max messages will be set to 1 (one).
    /// if reply option's timeout is not defined then it will be set to NatsOptions.RequestTimeout.
    /// </remarks>
    public static async ValueTask<NatsMsg?> RequestAsync(
        this NatsConnection nats,
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var opts = nats.SetReplyOptsDefaults(replyOpts);

        await using var sub = await nats.RequestSubAsync(subject, payload, requestOpts, opts, cancellationToken).ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Inbox subscription cancelled");
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }

        return null;
    }

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
        this NatsConnection nats,
        in NatsMsg msg,
        in NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestAsync(
            nats,
            msg.Subject,
            payload: new ReadOnlySequence<byte>(msg.Data),
            requestOpts: new NatsPubOpts
            {
                Headers = msg.Headers,
            },
            replyOpts,
            cancellationToken);

    private static NatsSubOpts SetReplyOptsDefaults(this NatsConnection nats, NatsSubOpts? replyOpts)
    {
        var opts = (replyOpts ?? default) with
        {
            MaxMsgs = 1,
        };

        if ((opts.Timeout ?? default) == default)
        {
            opts = opts with { Timeout = nats.Options.RequestTimeout };
        }

        if (!opts.CanBeCancelled.HasValue)
        {
            opts = opts with { CanBeCancelled = true, };
        }

        return opts;
    }
}
