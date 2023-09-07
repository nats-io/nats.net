using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <summary>
    /// Publishes and yields immediately unless the command channel is full in which case
    /// waits until there is space in command channel.
    /// </summary>
    internal ValueTask PubPostAsync(string subject, string? replyTo = default, ReadOnlySequence<byte> payload = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBytesCommand.Create(_pool, subject, replyTo, headers, payload, cancellationToken);
            return EnqueueCommandAsync(command);
        }
        else
        {
            return WithConnectAsync(subject, replyTo, headers, payload, cancellationToken, static (self, s, r, h, p, c) =>
            {
                var command = PublishBytesCommand.Create(self._pool, s, r, h, p, c);
                return self.EnqueueCommandAsync(command);
            });
        }
    }

    internal ValueTask PubModelPostAsync<T>(string subject, T? data, INatsSerializer serializer, string? replyTo = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default)
    {
        headers?.SetReadOnly();

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishCommand<T>.Create(_pool, subject, replyTo, headers, data, serializer, cancellationToken);
            return EnqueueCommandAsync(command);
        }
        else
        {
            return WithConnectAsync(subject, replyTo, headers, data, serializer, cancellationToken, static (self, s, r, h, d, ser, c) =>
            {
                var command = PublishCommand<T>.Create(self._pool, s, r, h, d, ser, c);
                return self.EnqueueCommandAsync(command);
            });
        }
    }

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

    internal ValueTask SubAsync(string subject, string? queueGroup, NatsSubOpts? opts, NatsSubBase sub, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return SubscriptionManager.SubscribeAsync(subject, queueGroup, opts, sub, cancellationToken);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, opts, sub, cancellationToken, static (self, s, q, o, b, token) =>
            {
                return self.SubscriptionManager.SubscribeAsync(s, q, o, b, token);
            });
        }
    }
}
