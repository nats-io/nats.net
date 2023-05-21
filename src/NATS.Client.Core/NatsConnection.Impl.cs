using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection : INatsCommand
{
    public ValueTask PublishAsync(string subject, ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var key = new NatsKey(subject, true);

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(_pool, GetCommandTimer(cancellationToken), key, data);
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
            return WithConnectAsync(key, data, cancellationToken, static (self, k, v, token) =>
            {
                var command = AsyncPublishBytesCommand.Create(self._pool, self.GetCommandTimer(token), k, v);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    public ValueTask PublishAsync(NatsMsg msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync(msg.Subject, msg.Data, default, cancellationToken);
    }

    public ValueTask PublishAsync<T>(string subject, T data, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var key = new NatsKey(subject, true);

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), key, data, opts?.Serializer ?? Options.Serializer);
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
            return WithConnectAsync(key, data, cancellationToken, static (self, k, v, token) =>
            {
                var command = AsyncPublishCommand<T>.Create(self._pool, self.GetCommandTimer(token), k, v, self.Options.Serializer);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    public ValueTask PublishAsync<T>(NatsMsg<T> msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(msg.Subject, msg.Data, default, cancellationToken);
    }

    public void PostPublish(string subject, ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default)
    {
        var key = new NatsKey(subject, true);

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBytesCommand.Create(_pool, GetCommandTimer(CancellationToken.None), key, data);
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(key, data, static (self, k, v) =>
            {
                var command = PublishBytesCommand.Create(self._pool, self.GetCommandTimer(CancellationToken.None), k, v);
                self.EnqueueCommandSync(command);
            });
        }
    }

    public void PostPublish(NatsMsg msg)
    {
        PostPublish(msg.Subject, msg.Data);
    }

    public void PostPublish<T>(string subject, T data, in NatsPubOpts? opts = default)
    {
        var key = new NatsKey(subject, true);

        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), key, data, opts?.Serializer ?? Options.Serializer);
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(key, data, opts, static (self, k, v, o) =>
            {
                var command = PublishCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), k, v, o?.Serializer ?? self.Options.Serializer);
                self.EnqueueCommandSync(command);
            });
        }
    }

    public void PostPublish<T>(NatsMsg<T> msg)
    {
        PostPublish(msg.Subject, msg.Data);
    }

    public ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask<NatsSub<T>> SubscribeAsync<T>(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
