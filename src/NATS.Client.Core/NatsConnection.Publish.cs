namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();
        if (ConnectionState != NatsConnectionState.Open)
            await ConnectAndPublishAsync(subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, cancellationToken).ConfigureAwait(false);
        else
            await CommandWriter.PublishAsync(subject, default, headers, replyTo, NatsRawSerializer<byte[]>.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.SerializerRegistry.GetSerializer<T>();
        headers?.SetReadOnly();
        if (ConnectionState != NatsConnectionState.Open)
            return ConnectAndPublishAsync(subject, data, headers, replyTo, serializer, cancellationToken);
        else
            return CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        PublishAsync(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);

    private async ValueTask ConnectAndPublishAsync<T>(string subject, T? data, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await CommandWriter.PublishAsync(subject, data, headers, replyTo, serializer, cancellationToken).ConfigureAwait(false);
    }
}
