using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var key = new NatsKey(subject, true);

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(_pool, GetCommandTimer(cancellationToken), key, payload);
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
            return WithConnectAsync(key, payload, cancellationToken, static (self, k, v, token) =>
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
        var key = new NatsKey(subject, true);

        NatsKey? replyTo = null;
        if (!string.IsNullOrEmpty(opts?.ReplyTo))
        {
            replyTo = new NatsKey(opts.Value.ReplyTo, true);
        }

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), key, replyTo, data, opts?.Serializer ?? Options.Serializer);
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
            return WithConnectAsync(key, replyTo, data, cancellationToken, static (self, k, r, v, token) =>
            {
                var command = AsyncPublishCommand<T>.Create(self._pool, self.GetCommandTimer(token), k, r, v, self.Options.Serializer);
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
