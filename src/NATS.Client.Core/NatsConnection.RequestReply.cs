using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public string NewInbox() => $"{InboxPrefix}{Guid.NewGuid():n}";

    /// <inheritdoc />
    public async ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var opts = SetReplyOptsDefaults(replyOpts);

        await using var sub = await RequestSubAsync<TRequest, TReply>(subject, data, requestOpts, opts, cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<NatsMsg?> RequestAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var opts = SetReplyOptsDefaults(replyOpts);

        await using var sub = await RequestSubAsync(subject, payload, requestOpts, opts, cancellationToken).ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var sub = await RequestSubAsync<TRequest, TReply>(subject, data, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
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

    /// <inheritdoc />
    public async IAsyncEnumerable<NatsMsg> RequestManyAsync(
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var sub = await RequestSubAsync(subject, payload, requestOpts, replyOpts, cancellationToken).ConfigureAwait(false);

        while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
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

    private NatsSubOpts SetReplyOptsDefaults(in NatsSubOpts? replyOpts)
    {
        var opts = (replyOpts ?? default) with { MaxMsgs = 1, };

        if ((opts.Timeout ?? default) == default)
        {
            opts = opts with { Timeout = Options.RequestTimeout };
        }

        return opts;
    }
}
