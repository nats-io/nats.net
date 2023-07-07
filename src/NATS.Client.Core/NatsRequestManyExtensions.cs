using System.Buffers;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsRequestManyExtensions
{
    // RequestManyAsync methods
    // Same as PublishAsync with the following changes
    // - Response is IAsyncEnumerable<NatsMsg>
    // - PubOpts is called requestOpts
    // - add SubOpts replyOpts
    //   - if replyOpts.Timeout == null then set to NatsOptions.RequestTimeout
    public static async IAsyncEnumerable<NatsMsg> RequestManyAsync(
        this NatsConnection nats,
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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
    }

    public static IAsyncEnumerable<NatsMsg> RequestManyAsync(
        this NatsConnection nats,
        NatsMsg msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestManyAsync(nats, msg.Subject, new ReadOnlySequence<byte>(msg.Data), default, replyOpts, cancellationToken);

    public static async IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        this NatsConnection nats,
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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
    }

    public static IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        this NatsConnection nats,
        NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestManyAsync<TRequest, TReply>(nats, msg.Subject, msg.Data, default, replyOpts, cancellationToken);
}
