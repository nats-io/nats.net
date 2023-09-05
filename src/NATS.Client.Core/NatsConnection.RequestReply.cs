using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    private static readonly NatsSubOpts DefaultReplyOpts = new() { MaxMsgs = 1 };

    /// <inheritdoc />
    public string NewInbox() => NewInbox(InboxPrefix);

    [SkipLocalsInit]
    private static string NewInbox(ReadOnlySpan<char> prefix)
    {
        Span<char> buffer = stackalloc char[64];
        uint separatorLength = prefix.Length > 0 ? 1u : 0u;
        uint totalLength = (uint)prefix.Length + (uint)NuidWriter.NUID_LENGTH + separatorLength;
        if (totalLength <= buffer.Length)
        {
             buffer = buffer.Slice(0, (int)totalLength);
        }
        else
        {
            buffer = new char[totalLength];
        }

        uint totalPrefixLength = (uint)prefix.Length + separatorLength;
        if ((uint)buffer.Length > totalPrefixLength && (uint)buffer.Length > (uint)prefix.Length)
        {
            prefix.CopyTo(buffer);
            buffer[prefix.Length] = '.';
            Span<char> remaining = buffer.Slice((int)totalPrefixLength);
            bool didWrite = NuidWriter.TryWriteNuid(remaining);
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
