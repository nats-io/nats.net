using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(_pool, GetCommandTimer(cancellationToken), subject, payload);
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
            return WithConnectAsync(subject, payload, cancellationToken, static (self, k, v, token) =>
            {
                var command = AsyncPublishBytesCommand.Create(self._pool, self.GetCommandTimer(token), k, v);
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

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), subject, replyTo, data, opts?.Serializer ?? Options.Serializer);
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
            return WithConnectAsync(subject, replyTo, data, cancellationToken, static (self, s, r, v, token) =>
            {
                var command = AsyncPublishCommand<T>.Create(self._pool, self.GetCommandTimer(token), s, r, v, self.Options.Serializer);
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
