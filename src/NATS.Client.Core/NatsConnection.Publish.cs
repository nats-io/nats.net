using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
        => PublishAsync(Telemetry.NatsActivities, subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, cancellationToken);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
        => PublishAsync(Telemetry.NatsActivities, msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, cancellationToken);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
        => PublishAsync(Telemetry.NatsActivities, subject, data, headers, replyTo, serializer ?? Opts.SerializerRegistry.GetSerializer<T>(), cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueTask PublishNoneAsync(ActivitySource activitySource, string subject, NatsHeaders? headers = default, string? replyTo = default, CancellationToken cancellationToken = default)
        => PublishAsync(activitySource, subject, data: default, headers, replyTo, NatsRawSerializer<byte[]>.Default, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueTask PublishAsync<T>(ActivitySource activitySource, string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        if (ConnectionState != NatsConnectionState.Open)
            return ConnectAndPublishAsync(activitySource, subject, data, headers, replyTo, serializer ?? Opts.SerializerRegistry.GetSerializer<T>(), cancellationToken);

        return InnerPublishAsync(activitySource, subject, data, headers, replyTo, serializer ?? Opts.SerializerRegistry.GetSerializer<T>(), cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask InnerPublishAsync<T>(ActivitySource activitySource, string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
        => activitySource.HasListeners()
            ? PublishTracedAsync(activitySource, subject, data, headers, replyTo, serializer, cancellationToken)
            : SendPublishAsync(subject, data, headers, replyTo, serializer, cancellationToken);

    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements should appear before instance elements", Justification = "Method is private")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask SendPublishAsync<T>(string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken = default)
    {
        Telemetry.AddTraceContextHeaders(Activity.Current, ref headers);
        headers?.SetReadOnly();

        return CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken);
    }

    [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements should appear before instance elements", Justification = "Method is private")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask PublishTracedAsync<T>(ActivitySource activitySource, string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartSendActivity(activitySource, Telemetry.Constants.PublishActivityName, this, subject, replyTo);

        try
        {
            await SendPublishAsync(subject, data, headers, replyTo, serializer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Telemetry.SetException(activity, ex);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ConnectAndPublishAsync<T>(ActivitySource activitySource, string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await InnerPublishAsync(activitySource, subject, data, headers, replyTo, serializer, cancellationToken).ConfigureAwait(false);
    }
}
