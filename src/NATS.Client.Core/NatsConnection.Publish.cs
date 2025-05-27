using System.Diagnostics;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishInternalAsync(subject, NatsRawSerializer<byte[]>.Default, default, headers, replyTo, cancellationToken);

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        return PublishInternalAsync(subject, serializer, data, headers, replyTo, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishInternalAsync(msg.Subject, serializer, msg.Data, msg.Headers, msg.ReplyTo, cancellationToken);

    private ValueTask PublishInternalAsync<T>(string subject, INatsSerialize<T> serializer, T? data, NatsHeaders? headers = default, string? replyTo = default, CancellationToken cancellationToken = default)
    {
        var start = DateTimeOffset.UtcNow;
        using var activity = Telemetry.StartSendActivity(start, $"{SpanDestinationName(subject)} {Telemetry.Constants.PublishActivityName}", this, subject, replyTo);
        Task task;

        //code to create the message to be sent and record as a span + metric

        try
        {
            Telemetry.AddTraceContextHeaders(activity, ref headers);
            headers?.SetReadOnly();
            task = ConnectionState != NatsConnectionState.Open
                ? ConnectAndPublishAsync(subject, data, headers, replyTo, serializer, cancellationToken)
                : CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken);
        }
        catch (Exception ex)
        {
            Telemetry.SetException(activity, ex);
            throw;
        }
        finally
        {
            var end = DateTimeOffset.UtcNow;
            activity?.SetEndTime(end.UtcDateTime);
            var duration = end - start;
            Telemetry.RecordOperationDuration(duration.TotalSeconds, activity.TagObjects.ToArray());
        }
        return task;
    }

    private async ValueTask ConnectAndPublishAsync<T>(string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken).ConfigureAwait(false);
    }
}
