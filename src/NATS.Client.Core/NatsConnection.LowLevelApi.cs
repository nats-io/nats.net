using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    internal ValueTask PubAsync(string subject, string? replyTo = default, ReadOnlySequence<byte> payload = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(_pool, GetCancellationTimer(cancellationToken), subject, replyTo, headers, payload);
            if (TryEnqueueCommand(command))
            {
                return command.AsValueTask();
            }
            else
            {
                return EnqueueAndAwaitCommandAsync(command);
            }
        }
        else
        {
            return WithConnectAsync(subject, replyTo, headers, payload, cancellationToken, static (self, s, r, h, p, token) =>
            {
                var command = AsyncPublishBytesCommand.Create(self._pool, self.GetCancellationTimer(token), s, r, h, p);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    internal ValueTask PubModelAsync<T>(string subject, T? data, INatsSerializer serializer, string? replyTo = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(_pool, GetCancellationTimer(cancellationToken), subject, replyTo, headers, data, serializer);
            if (TryEnqueueCommand(command))
            {
                return command.AsValueTask();
            }
            else
            {
                return EnqueueAndAwaitCommandAsync(command);
            }
        }
        else
        {
            return WithConnectAsync(subject, replyTo, headers, data, serializer, cancellationToken, static (self, s, r, h, v, ser, token) =>
            {
                var command = AsyncPublishCommand<T>.Create(self._pool, self.GetCancellationTimer(token), s, r, h, v, ser);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    internal ValueTask<T> SubAsync<T>(string subject, string? queueGroup, INatsSubBuilder<T> builder, CancellationToken cancellationToken = default)
        where T : INatsSub
    {
        var sub = builder.Build(subject, queueGroup, this, _subscriptionManager);
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.SubscribeAsync(subject, queueGroup, sub, cancellationToken);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, sub, cancellationToken, static (self, s, qg, sb, token) =>
            {
                return self._subscriptionManager.SubscribeAsync(s, qg, sb, token);
            });
        }
    }
}
