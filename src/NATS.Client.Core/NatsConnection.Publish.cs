using System.Diagnostics;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        if (Telemetry.HasListeners())
        {
            using var activity = Telemetry.StartSendActivity($"{SpanDestinationName(subject)} {Telemetry.Constants.PublishActivityName}", this, subject, replyTo);
            Telemetry.AddTraceContextHeaders(activity, ref headers);
            try
            {
                headers?.SetReadOnly();
                return PerformPublishAsync(subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, false, cancellationToken);
            }
            catch (Exception ex)
            {
                Telemetry.SetException(activity, ex);
                throw;
            }
        }

        headers?.SetReadOnly();
        return PerformPublishAsync(subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, false, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        if (Telemetry.HasListeners())
        {
            using var activity = Telemetry.StartSendActivity($"{SpanDestinationName(subject)} {Telemetry.Constants.PublishActivityName}", this, subject, replyTo);
            Telemetry.AddTraceContextHeaders(activity, ref headers);
            try
            {
                serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
                headers?.SetReadOnly();
                return PerformPublishAsync(subject, data, headers, replyTo, serializer, false, cancellationToken);
            }
            catch (Exception ex)
            {
                Telemetry.SetException(activity, ex);
                throw;
            }
        }

        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        headers?.SetReadOnly();
        return PerformPublishAsync<T>(subject, data, headers, replyTo, serializer, opts?.Priority ?? false, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishAsync(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);

    private async ValueTask PerformPublishAsync<T>(string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, bool priority = false, CancellationToken cancellationToken = default)
    {
        NatsPooledBufferWriter<byte>? headersBuffer = null;
        NatsPooledBufferWriter<byte>? payloadBuffer = null;

        try
        {
            if (headers != null)
            {
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
            await DoPublishAsync(subject, headersBuffer?.WrittenMemory, payloadBuffer.WrittenMemory, replyTo, priority, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Telemetry.SetException(Activity.Current, ex);
            throw;
        }
        finally
        {
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
        }
    }

    private async ValueTask DoPublishAsync(string subject, ReadOnlyMemory<byte>? headers = default, ReadOnlyMemory<byte> data = default, string? replyTo = default, bool priority = false, CancellationToken cancellationToken = default)
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!priority)
        {
            await CommandWriter.PublishAsync(subject, headers, data, replyTo, cancellationToken).ConfigureAwait(false);
        }
        else if (_priorityCommandWriter != null)
        {
            await _priorityCommandWriter.PublishAsync(subject, headers, data, replyTo, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using (var priorityCommandWriter = new PriorityCommandWriter(this, _pool, _socketConnection!, Opts, Counter, EnqueuePing))
            {
                await priorityCommandWriter.CommandWriter.PublishAsync(subject, headers, data, replyTo, cancellationToken).ConfigureAwait(false);
            }
        }

        return;
    }
}
