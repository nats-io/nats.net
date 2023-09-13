using System.Buffers;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    private static readonly NatsSubOpts DefaultReplyOpts = new() { MaxMsgs = 1 };

    /// <inheritdoc />
    public string NewInbox() => $"{InboxPrefix}{Guid.NewGuid():n}";

    /// <inheritdoc />
    public async ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var opts = SetReplyOptsDefaults(replyOpts);

        await using var sub = await RequestSubAsync<TRequest, TReply>(subject, data, headers, requestOpts, opts, cancellationToken)
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
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var opts = SetReplyOptsDefaults(replyOpts);

        await using var sub = await RequestSubAsync(subject, payload, headers, requestOpts, opts, cancellationToken).ConfigureAwait(false);

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
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var sub = await RequestSubAsync<TRequest, TReply>(subject, data, headers, requestOpts, replyOpts, cancellationToken)
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
        NatsHeaders? headers = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var sub = await RequestSubAsync(subject, payload, headers, requestOpts, replyOpts, cancellationToken).ConfigureAwait(false);

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

    private NatsSubOpts SetReplyOptsDefaults(NatsSubOpts? replyOpts)
    {
        var opts = replyOpts ?? DefaultReplyOpts;

        if ((opts.Timeout ?? default) == default)
        {
            opts = opts with { Timeout = Opts.RequestTimeout };
        }

        return opts;
    }
}
