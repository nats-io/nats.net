using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

                if (await sub1.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (sub1.Msgs.TryRead(out var msg))
                    {
                        return msg;
                    }
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

        if (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                return msg;
            }
        }

        throw new NatsNoReplyException();
    }

    //
    //
    public async ValueTask<NatsMsg<TReply>> RequestAsync2<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        var (tcs, replyTo) = await RequestManager.NewRequestAsync(cancellationToken).ConfigureAwait(false);

        await PublishAsync(subject, data, headers, replyTo, requestSerializer, requestOpts, cancellationToken).ConfigureAwait(false);

        // var msgBytes = await req.Tcs.Task.ConfigureAwait(false);

        var msgBytes = await tcs.RunAsync().ConfigureAwait(false);

        if (msgBytes.Headers?.Code == 503)
        {
            throw new NatsNoRespondersException();
        }

        using var memoryOwner = msgBytes.Data;

        // var replyData = (replySerializer ?? NatsDefaultSerializer<TReply>.Default)
        //     .Deserialize(new ReadOnlySequence<byte>(memoryOwner.Memory));

        return new NatsMsg<TReply>(
            msgBytes.Subject,
            msgBytes.ReplyTo,
            msgBytes.Size,
            msgBytes.Headers,
            default, // replyData,
            msgBytes.Connection);
    }

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

        while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (sub.Msgs.TryRead(out var msg))
            {
                yield return msg;
            }
        }
    }

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

    [SkipLocalsInit]
    internal static string NewInbox(ReadOnlySpan<char> prefix, long id)
    {
        Span<char> buffer = stackalloc char[64];
        Span<byte> idBuffer = stackalloc byte[32];

        if (!Utf8Formatter.TryFormat(id, idBuffer, out var idLength))
        {
            return Throw();
        }

        var separatorLength = prefix.Length > 0 ? 1u : 0u;
        var totalLength = (uint)prefix.Length + (uint)idLength + separatorLength;
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

            var bs = idBuffer.Slice(0, idLength);
            for (var index = 0; index < bs.Length; index++)
            {
                var b = bs[index];
                remaining[index] = (char)b;
            }

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
