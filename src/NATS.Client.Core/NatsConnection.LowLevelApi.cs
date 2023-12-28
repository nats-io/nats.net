using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal async ValueTask PubModelPostAsync<T>(string subject, T? data, INatsSerialize<T> serializer, string? replyTo = default, NatsHeaders? headers = default, Action<Exception>? errorHandler = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().ConfigureAwait(false);
        }

        await CommandWriter.PublishAsync(subject, replyTo, headers, data, serializer, cancellationToken).ConfigureAwait(false);
    }

    internal ValueTask PubAsync(string subject, string? replyTo = default, ReadOnlySequence<byte> payload = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default) =>
        PubModelAsync(subject, payload, NatsRawSerializer<ReadOnlySequence<byte>>.Default, replyTo, headers, cancellationToken);

    internal async ValueTask PubModelAsync<T>(string subject, T? data, INatsSerialize<T> serializer, string? replyTo = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().ConfigureAwait(false);
        }

        await CommandWriter.PublishAsync(subject, replyTo, headers, data, serializer, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().ConfigureAwait(false);
        }

        await SubscriptionManager.SubscribeAsync(sub, cancellationToken).ConfigureAwait(false);
    }
}
