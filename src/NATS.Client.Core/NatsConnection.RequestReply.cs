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
        CancellationToken cancellationToken = default) =>
        await RequestInternalAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, null, replyOpts, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask<NatsMsg<TReply>> RequestAsync<TReply>(
        string subject,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestInternalAsync<object, TReply>(subject, default, default, default, replySerializer, default, replyOpts, cancellationToken);

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

    private async ValueTask<NatsMsg<TReply>> RequestInternalAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPublishProps? props = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        props ??= new NatsPublishProps(subject);
        props.InboxPrefix ??= InboxPrefix;

        var start = DateTimeOffset.UtcNow;
        var tags = Telemetry.GetTags(ServerInfo, props);
        using var activity = Telemetry.StartActivity(start, props, ServerInfo, Telemetry.Constants.RequestReplyActivityName, tags);
        try
        {
            Telemetry.AddTraceContextHeaders(activity, ref headers);

            if (Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
            {
                return await RequestDirectInternalAsync(props, data, headers, requestSerializer, replySerializer, replyOpts, activity, cancellationToken).ConfigureAwait(false);
            }

            replyOpts = SetReplyManyOptsDefaults(replyOpts);
            requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();
            replySerializer ??= Opts.SerializerRegistry.GetDeserializer<TReply>();
            await using var sub = await CreateRequestSubInternalAsync<TRequest, TReply>(props, data, headers, requestSerializer, replySerializer, replyOpts, cancellationToken)
                    .ConfigureAwait(false);

            await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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
        finally
        {
            var end = DateTimeOffset.UtcNow;
            var duration = end - start;
            activity?.SetEndTime(end.DateTime);
            Telemetry.RecordOperationDuration(Telemetry.Constants.RequestReplyActivityName, duration, tags);
        }
    }

    private async ValueTask<NatsMsg<TReply>> RequestDirectInternalAsync<TRequest, TReply>(
        NatsPublishProps props,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsSubOpts? replyOpts = default,
        Activity? activity = default,
        CancellationToken cancellationToken = default)
    {
        try
        {
            replyOpts = SetReplyOptsDefaults(replyOpts);
            requestSerializer ??= Opts.SerializerRegistry.GetSerializer<TRequest>();

            using var rt = _replyTaskFactory.CreateReplyTask(replySerializer, replyOpts.Timeout);

            // to do add in reply to tags on both activity and array before invoking
            await PublishInternalAsync(props, requestSerializer, data, headers, activity, cancellationToken).ConfigureAwait(false);
            return await rt.GetResultAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Telemetry.SetException(activity, e);
            throw;
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
