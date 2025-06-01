using System.Diagnostics;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishAsync(subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, default, cancellationToken);

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        var props = new NatsPublishProps(subject)
        {
            InboxPrefix = InboxPrefix,
        };
        props.SetReplyTo(replyTo);
        return PublishInternalAsync(props, serializer, data, headers, default, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PublishAsync(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);
    }

    private ValueTask PublishInternalAsync<T>(NatsPublishProps props, INatsSerialize<T> serializer, T? data, NatsHeaders? headers = default, Activity? activity = default, CancellationToken cancellationToken = default)
    {
        DateTimeOffset? publishStart = null;
        List<KeyValuePair<string, object?>> tags;
        if (activity == null)
        {
            publishStart = DateTimeOffset.UtcNow;
            tags = Telemetry.GetTags(ServerInfo, props);
            using var publishActivity = Telemetry.StartActivity(publishStart.Value, props, ServerInfo, Telemetry.Constants.PublishActivityName, tags);
            activity = publishActivity;
        }
        else
        {
            tags = activity.TagObjects.ToList();
        }

        var createStart = DateTimeOffset.UtcNow;
        DateTimeOffset? createEnd = null;
        using var createActivity = Telemetry.StartActivity(createStart, props, ServerInfo, Telemetry.Constants.CreateActivityName, tags);

        ValueTask task;

        NatsPooledBufferWriter<byte>? headersBuffer = null;
        NatsPooledBufferWriter<byte>? payloadBuffer = null;

        try
        {
            if (headers != null)
            {
                Telemetry.AddTraceContextHeaders(activity, ref headers);
                headers?.SetReadOnly();
                if (!_pool.TryRent(out headersBuffer))
                    headersBuffer = new NatsPooledBufferWriter<byte>(_arrayPoolInitialSize);
            }

            if (!_pool.TryRent(out payloadBuffer!))
                payloadBuffer = new NatsPooledBufferWriter<byte>(_arrayPoolInitialSize);

            if (headers != null)
                _headerWriter.Write(headersBuffer!, headers);

            if (data != null)
                serializer.Serialize(payloadBuffer, data);

            createEnd = DateTimeOffset.UtcNow;
            props.HeaderLength = headersBuffer?.WrittenMemory.Length ?? 0;
            props.TotalMessageLength = props.HeaderLength + payloadBuffer.WrittenMemory.Length;
            task = PublishInternalAsync(props, payloadBuffer.WrittenMemory, headersBuffer?.WrittenMemory, cancellationToken);
        }
        catch (Exception ex)
        {
            if (createEnd == null)
            {
                Telemetry.SetException(createActivity, ex);
            }
            else
            {
                Telemetry.SetException(activity, ex);
            }

            throw;
        }
        finally
        {
            createEnd ??= DateTimeOffset.UtcNow;
            createActivity?.SetEndTime(createEnd.Value.UtcDateTime);
            var createDuration = createEnd.Value - createStart;
            Telemetry.RecordOperationDuration(Telemetry.Constants.CreateActivityName, createDuration, tags);
            Telemetry.AddSentMessages(1, tags);
            Telemetry.RecordSentBytes(props.TotalEnvelopeLength, tags);

            if (payloadBuffer != null)
            {
                payloadBuffer.Reset();
                _pool.Return(payloadBuffer);
            }

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }

            if (publishStart != null)
            {
                var publishEnd = DateTimeOffset.UtcNow;
                activity?.SetEndTime(publishEnd.UtcDateTime);
                var publishDuration = publishEnd - publishStart.Value;
                Telemetry.RecordOperationDuration(Telemetry.Constants.PublishActivityName, publishDuration, tags);
            }
        }

        return task;
    }

    private ValueTask PublishInternalAsync(NatsPublishProps props, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte>? headers = null, CancellationToken cancellationToken = default)
    {
        return ConnectionState != NatsConnectionState.Open
            ? ConnectAndPublishAsync(props, data, headers, cancellationToken)
            : CommandWriter.PublishAsync(props, data, headers, cancellationToken);
    }

    private async ValueTask ConnectAndPublishAsync(NatsPublishProps props, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte>? headers = null, CancellationToken cancellationToken = default)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await CommandWriter.PublishAsync(props, data, headers, cancellationToken).ConfigureAwait(false);
    }
}
