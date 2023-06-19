using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var replyTo = opts?.ReplyTo;
        var headers = opts?.Headers;

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(_pool, GetCommandTimer(cancellationToken), subject, replyTo, headers, payload);
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
                var command = AsyncPublishBytesCommand.Create(self._pool, self.GetCommandTimer(token), s, r, h, p);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(NatsMsg msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync(msg.Subject, msg.Data, default, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T data, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var replyTo = opts?.ReplyTo;
        var serializer = opts?.Serializer ?? Options.Serializer;
        var headers = opts?.Headers;

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), subject, replyTo, headers, data, serializer);
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
                var command = AsyncPublishCommand<T>.Create(self._pool, self.GetCommandTimer(token), s, r, h, v, ser);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(NatsMsg<T> msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(msg.Subject, msg.Data, default, cancellationToken);
    }
}
