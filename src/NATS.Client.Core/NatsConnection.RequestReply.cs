using System.Buffers;
using System.Diagnostics;
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

                if (Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
                {
                    using var rt = _replyTaskFactory.CreateReplyTask(replySerializer, replyOpts.Timeout);
                    requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
                    await PublishAsync(subject, data, headers, rt.Subject, requestSerializer, requestOpts, cancellationToken).ConfigureAwait(false);
                    var msg = await rt.GetResultAsync(cancellationToken).ConfigureAwait(false);
                    msg.Headers?.Activity?.Dispose();
                    return msg;
                }

                await using var sub1 = await CreateRequestSubAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
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

        if (Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
        {
            using var rt = _replyTaskFactory.CreateReplyTask(replySerializer, replyOpts.Timeout);
            requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
            await PublishAsync(subject, data, headers, rt.Subject, requestSerializer, requestOpts, cancellationToken).ConfigureAwait(false);
            return await rt.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var sub = await CreateRequestSubAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
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
        await using var sub = await CreateRequestSubAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return msg;
        }
    }

    [SkipLocalsInit]
    internal static string NewInbox(string prefix)
    {
        static void WriteBuffer(Span<char> buffer, (string prefix, int pfxLen) state)
        {
            state.prefix.AsSpan().CopyTo(buffer);
            buffer[state.prefix.Length] = '.';
            var remaining = buffer.Slice(state.pfxLen);
            var didWrite = Nuid.TryWriteNuid(remaining);
            Debug.Assert(didWrite, "didWrite");
        }

        var separatorLength = prefix.Length > 0 ? 1 : 0;
        var totalLength = prefix.Length + (int)Nuid.NuidLength + separatorLength;
        var totalPrefixLength = prefix.Length + separatorLength;

#if NET6_0_OR_GREATER || NETSTANDARD2_1
        return string.Create(totalLength, (prefix, totalPrefixLength), (buf, state) => WriteBuffer(buf, state));
#else
        Span<char> buffer = new char[totalLength];
        WriteBuffer(buffer, (prefix, totalPrefixLength));
        return buffer.ToString();
#endif
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
