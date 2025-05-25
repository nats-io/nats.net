using System.Diagnostics;
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
        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        ValueTask task;
        using var activity = Telemetry.StartSendActivity($"{SpanDestinationName(subject)} {Telemetry.Constants.PublishActivityName}", this, subject, replyTo);

        if (activity != null)
        {
            try
            {
                Telemetry.AddTraceContextHeaders(activity, ref headers);
                task = PublishImpAsync(subject, serializer, data, headers, replyTo, cancellationToken);
                Telemetry.RecordOperationDuration(activity.Duration.TotalSeconds, activity.TagObjects.ToArray());
            }
            catch (Exception ex)
            {
                Telemetry.SetException(activity, ex);
                throw;
            }
        }
        else
        {
            task = PublishImpAsync(subject, serializer, data, headers, replyTo, cancellationToken);
        }

        return task;
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishAsync(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);

    private ValueTask PublishImpAsync<T>(string subject, INatsSerialize<T> serializer, T? data, NatsHeaders? headers = default, string? replyTo = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();
        return ConnectionState != NatsConnectionState.Open
            ? ConnectAndPublishAsync(subject, data, headers, replyTo, serializer, cancellationToken)
            : CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken);
    }

    private async ValueTask ConnectAndPublishAsync<T>(string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken).ConfigureAwait(false);
    }
}
