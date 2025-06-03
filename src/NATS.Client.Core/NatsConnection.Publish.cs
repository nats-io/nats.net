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
                return PerformPublishAsync(subject, headers, default, NatsRawSerializer<byte[]>.Default, replyTo, cancellationToken);
            }
            catch (Exception ex)
            {
                Telemetry.SetException(activity, ex);
                throw;
            }
        }

        headers?.SetReadOnly();
        return PerformPublishAsync(subject, headers, default, NatsRawSerializer<byte[]>.Default, replyTo, cancellationToken);
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
                return PerformPublishAsync(subject, headers, data, serializer, replyTo, cancellationToken);
            }
            catch (Exception ex)
            {
                Telemetry.SetException(activity, ex);
                throw;
            }
        }

        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        headers?.SetReadOnly();
        return PerformPublishAsync<T>(subject, headers, data, serializer, replyTo, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishAsync(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);

    private async ValueTask ConnectAndPublishAsync(string subject, ReadOnlyMemory<byte>? headers, ReadOnlyMemory<byte> data, string? replyTo, CancellationToken cancellationToken)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await CommandWriter.PublishAsync(subject, headers, data, replyTo, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask PerformPublishAsync<T>(string subject, NatsHeaders? headers, T? data, INatsSerialize<T> serializer, string? replyTo = default, CancellationToken cancellationToken = default)
    {
        NatsPooledBufferWriter<byte>? headersBuffer = null;
        NatsPooledBufferWriter<byte>? payloadBuffer = null;

        ValueTask task;

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
            task = DoPublishAsync(subject, headersBuffer?.WrittenMemory, payloadBuffer.WrittenMemory, replyTo, cancellationToken);
        }
        catch (Exception ex)
        {
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

        return task;
    }

    private ValueTask DoPublishAsync(string subject, ReadOnlyMemory<byte>? headers = default, ReadOnlyMemory<byte> data = default, string? replyTo = default, CancellationToken cancellationToken = default)
    {
        return ConnectionState != NatsConnectionState.Open
            ? ConnectAndPublishAsync(subject, headers, data, replyTo, cancellationToken)
            : CommandWriter.PublishAsync(subject, headers, data, replyTo, cancellationToken);
    }
}
