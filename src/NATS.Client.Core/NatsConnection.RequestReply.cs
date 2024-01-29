using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    private static readonly NatsSubOpts ReplyOptsDefault = new NatsSubOpts { MaxMsgs = 1, ThrowIfNoResponders = true, };

    private static readonly NatsSubOpts ReplyManyOptsDefault = new NatsSubOpts { StopOnEmptyMsg = true, ThrowIfNoResponders = true, };

    /// <inheritdoc />
    public string NewInbox() => NewInbox(InboxPrefix);

    /// <inheritdoc />
    public ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
        => RequestAsync<TRequest, TReply>(
            Telemetry.NatsActivities, subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken);

    internal async ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        ActivitySource activitySource,
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        replyOpts = SetReplyOptsDefaults(replyOpts);
        await using var sub = await RequestSubAsync<TRequest, TReply>(activitySource, subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        throw new NatsNoReplyException();
    }

    /// <inheritdoc />
    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1202:Elements should be ordered by access", Justification = "Internal is wrapped by public")]
    public async IAsyncEnumerable<NatsMsg<TReply>> RequestManyAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        replyOpts = SetReplyManyOptsDefaults(replyOpts);
        await using var sub = await RequestSubAsync<TRequest, TReply>(Telemetry.NatsActivities, subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (sub.Msgs.TryRead(out var msg))
            {
                yield return msg;
            }
        }
    }

    [SkipLocalsInit]
    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements should appear before instance elements", Justification = "This is a helper method")]
    internal static string NewInbox(ReadOnlySpan<char> prefix)
    {
        Span<char> buffer = stackalloc char[64];
        var separatorLength = prefix.Length > 0 ? 1u : 0u;
        var totalLength = (uint)prefix.Length + (uint)NuidWriter.NuidLength + separatorLength;
        if (totalLength <= buffer.Length)
        {
            buffer = buffer[..(int)totalLength];
        }
        else
        {
            buffer = new char[totalLength];
        }

        var totalPrefixLength = (uint)prefix.Length + separatorLength;
        if ((uint)buffer.Length > totalPrefixLength && (uint)buffer.Length > (uint)prefix.Length)
        {
            prefix.CopyTo(buffer);
            buffer[prefix.Length] = '.';
            var remaining = buffer[(int)totalPrefixLength..];
            var didWrite = NuidWriter.TryWriteNuid(remaining);
            Debug.Assert(didWrite, "didWrite");
            return new string(buffer);
        }

        return Throw();

        [DoesNotReturn]
        string Throw()
        {
            Debug.Fail("Must not happen");
            throw new InvalidOperationException("This should never be raised!");
        }
    }

    private NatsSubOpts SetReplyOptsDefaults(NatsSubOpts? replyOpts)
    {
        var opts = replyOpts ?? ReplyOptsDefault;
        if (!opts.MaxMsgs.HasValue)
        {
            opts = opts with { MaxMsgs = 1 };
        }

        return SetBaseReplyOptsDefaults(opts);
    }

    private NatsSubOpts SetReplyManyOptsDefaults(NatsSubOpts? replyOpts)
    {
        var opts = replyOpts ?? ReplyManyOptsDefault;
        if (!opts.StopOnEmptyMsg.HasValue)
        {
            opts = opts with { StopOnEmptyMsg = true };
        }

        return SetBaseReplyOptsDefaults(opts);
    }

    private NatsSubOpts SetBaseReplyOptsDefaults(NatsSubOpts opts)
    {
        if (!opts.Timeout.HasValue)
        {
            opts = opts with { Timeout = Opts.RequestTimeout };
        }

        if (!opts.ThrowIfNoResponders.HasValue)
        {
            opts = opts with { ThrowIfNoResponders = true };
        }

        return opts;
    }
}
