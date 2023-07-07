using System.Buffers;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsRequestSingleExtensions
{
    // RequestAsync methods
    // Same as PublishAsync with the following changes
    // - Response is 0 (null) or 1 NatsMsg
    // - PubOpts is called requestOpts
    // - add SubOpts replyOpts
    //   - default replyOpts.MaxMsgs to 1
    //   - if replyOpts.Timeout == null then set to NatsOptions.RequestTimeout
    public static async ValueTask<NatsMsg<TReply?>?> RequestSingleAsync<TRequest, TReply>(
        this NatsConnection nats,
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        await using var sub = await nats.RequestAsync<TRequest, TReply>(subject, data, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        return null;
    }

    public static ValueTask<NatsMsg<TReply?>?> RequestSingleAsync<TRequest, TReply>(
        this NatsConnection nats,
        in NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestSingleAsync<TRequest, TReply>(nats, msg.Subject, data: msg.Data, replyOpts: replyOpts, cancellationToken: cancellationToken);

    public static async ValueTask<NatsMsg?> RequestSingleAsync(
        this NatsConnection nats,
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        await using var sub = await nats.RequestAsync(subject, payload, requestOpts, replyOpts, cancellationToken).ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        return null;
    }

    public static ValueTask<NatsMsg?> RequestSingleAsync(
        this NatsConnection nats,
        in NatsMsg msg,
        in NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestSingleAsync(nats, msg.Subject, new ReadOnlySequence<byte>(msg.Data), default, replyOpts, cancellationToken);
}
