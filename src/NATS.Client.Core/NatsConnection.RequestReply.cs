using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    private static readonly NatsSubOpts ReplyOptsDefault = new NatsSubOpts
    {
        MaxMsgs = 1,
        ThrowIfNoResponders = true,
    };

    private static readonly NatsSubOpts ReplyManyOptsDefault = new NatsSubOpts
    {
        StopOnEmptyMsg = true,
        ThrowIfNoResponders = true,
    };

    /// <inheritdoc />
    public string NewInbox() => NewInbox(InboxPrefix);

    /// <inheritdoc />
    public async ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        if (Telemetry.HasListeners())
        {
            using var activity = Telemetry.StartSendActivity($"{SpanDestinationName(subject)} {Telemetry.Constants.RequestReplyActivityName}", this, subject, null);
            try
            {
                replyOpts = SetReplyOptsDefaults(replyOpts);
                await using var sub1 = await RequestSubAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
                    .ConfigureAwait(false);

                await foreach (var msg in sub1.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    return msg;
                }

                throw new NatsNoReplyException();
            }
            catch (Exception e)
            {
                Telemetry.SetException(activity, e);
                throw;
            }
        }

        replyOpts = SetReplyOptsDefaults(replyOpts);
        await using var sub = await RequestSubAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            return msg;
        }

        throw new NatsNoReplyException();
    }

    /// <inheritdoc />
    public ValueTask<NatsMsg<TReply>> RequestAsync<TReply>(
        string subject,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestAsync<object, TReply>(
            subject: subject,
            data: default,
            headers: default,
            requestSerializer: default,
            replySerializer: replySerializer,
            requestOpts: default,
            replyOpts: replyOpts,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
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
        await using var sub = await RequestSubAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return msg;
        }
    }

#if NETSTANDARD2_0
    internal static string NewInbox(string prefix) => NewInbox(prefix.AsSpan());
#endif

    [SkipLocalsInit]
    internal static string NewInbox(ReadOnlySpan<char> prefix)
    {
        Span<char> buffer = stackalloc char[64];
        var separatorLength = prefix.Length > 0 ? 1u : 0u;
        var totalLength = (uint)prefix.Length + (uint)NuidWriter.NuidLength + separatorLength;
        if (totalLength <= buffer.Length)
        {
            buffer = buffer.Slice(0, (int)totalLength);
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
            var remaining = buffer.Slice((int)totalPrefixLength);
            var didWrite = NuidWriter.TryWriteNuid(remaining);
            Debug.Assert(didWrite, "didWrite");
#if NETSTANDARD2_0
            return new string(buffer.ToArray());
#else
            return new string(buffer);
#endif
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
