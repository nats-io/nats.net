using System.Diagnostics;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PublishAsync(subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, opts, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var props = opts?.Props ?? new NatsPublishProps(subject, InboxPrefix);
        props.SetReplyTo(replyTo);
        return PublishAsync(props, data, headers, serializer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PublishAsync(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);
    }

    /// <inheritdoc />
    internal ValueTask PublishAsync<T>(NatsPublishProps props, T? data, NatsHeaders? headers, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        if (data == null)
        {
            serializer ??= NatsRawSerializer<T>.Default;
        }
        else
        {
            serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        }

        if (Telemetry.HasListeners())
        {
            using var activity = Telemetry.StartSendActivity($"{SpanDestinationName(props.Subject.ToString())} {Telemetry.Constants.PublishActivityName}", this, props.Subject.ToString(), props.ReplyTo?.ToString());
            Telemetry.AddTraceContextHeaders(activity, ref headers);
            try
            {
                headers?.SetReadOnly();
                return ConnectionState != NatsConnectionState.Open
                    ? ConnectAndPublishAsync(props, data, headers, serializer, cancellationToken)
                    : CommandWriter.PublishAsync(props, data, headers, serializer, cancellationToken);
            }
            catch (Exception ex)
            {
                Telemetry.SetException(activity, ex);
                throw;
            }
        }

        headers?.SetReadOnly();
        return ConnectionState != NatsConnectionState.Open
            ? ConnectAndPublishAsync(props, data, headers, serializer, cancellationToken)
            : CommandWriter.PublishAsync(props, data, headers, serializer, cancellationToken);
    }

    private async ValueTask ConnectAndPublishAsync<T>(NatsPublishProps props, T? data, NatsHeaders? headers, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await CommandWriter.PublishAsync(props, data, headers, serializer, cancellationToken).ConfigureAwait(false);
    }
}
