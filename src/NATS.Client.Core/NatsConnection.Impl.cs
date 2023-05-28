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

    public ValueTask PublishAsync<T>(NatsMsg<T> msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(msg.Subject, msg.Data, default, cancellationToken);
    }

    // public void PostPublish(string subject, ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default)
    // {
    //     var key = new NatsKey(subject, true);
    //
    //     if (ConnectionState == NatsConnectionState.Open)
    //     {
    //         var command = PublishBytesCommand.Create(_pool, GetCommandTimer(CancellationToken.None), key, data);
    //         EnqueueCommandSync(command);
    //     }
    //     else
    //     {
    //         WithConnect(key, data, static (self, k, v) =>
    //         {
    //             var command = PublishBytesCommand.Create(self._pool, self.GetCommandTimer(CancellationToken.None), k, v);
    //             self.EnqueueCommandSync(command);
    //         });
    //     }
    // }

    // public void PostPublish(NatsMsg msg)
    // {
    //     PostPublish(msg.Subject, msg.Data);
    // }

    // public void PostPublish<T>(string subject, T data, in NatsPubOpts? opts = default)
    // {
    //     var key = new NatsKey(subject, true);
    //
    //     if (ConnectionState == NatsConnectionState.Open)
    //     {
    //         var command = PublishCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), key, data, opts?.Serializer ?? Options.Serializer);
    //         EnqueueCommandSync(command);
    //     }
    //     else
    //     {
    //         WithConnect(key, data, opts, static (self, k, v, o) =>
    //         {
    //             var command = PublishCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), k, v, o?.Serializer ?? self.Options.Serializer);
    //             self.EnqueueCommandSync(command);
    //         });
    //     }
    // }

    // public void PostPublish<T>(NatsMsg<T> msg)
    // {
    //     PostPublish(msg.Subject, msg.Data);
    // }
    public ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var natsSub = new NatsSub
        {
            Subject = subject,
            Connection = this,
            QueueGroup = opts?.QueueGroup ?? string.Empty,
        };

        NatsKey? queueGroup = null;
        if (!string.IsNullOrWhiteSpace(opts?.QueueGroup))
        {
            queueGroup = new NatsKey(opts.Value.QueueGroup);
        }

        if (ConnectionState == NatsConnectionState.Open)
        {
            natsSub.InternalSubscription = _subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(subject, queueGroup, natsSub, cancellationToken);
            return new ValueTask<NatsSub>(natsSub);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, natsSub, cancellationToken, static (self, key, qg, handler, token) =>
            {
                handler.InternalSubscription = self._subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(key, qg, handler, token);
                return new ValueTask<NatsSub>(handler);
            });
        }
    }

    // XXX
    // public ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler, CancellationToken cancellationToken = default)
    // {
    //     if (ConnectionState == NatsConnectionState.Open)
    //     {
    //         return _subscriptionManager.AddAsync<T>(key, null, handler, cancellationToken);
    //     }
    //     else
    //     {
    //         return WithConnectAsync(key, handler, cancellationToken, static (self, key, handler, token) =>
    //         {
    //             return self._subscriptionManager.AddAsync<T>(key, null, handler, token);
    //         });
    //     }
    // }
    public ValueTask<NatsSub<T>> SubscribeAsync<T>(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var natsSub = new NatsSub<T>
        {
            Subject = subject,
            Connection = this,
            QueueGroup = opts?.QueueGroup ?? string.Empty,
        };

        NatsKey? queueGroup = null;
        if (!string.IsNullOrWhiteSpace(opts?.QueueGroup))
        {
            queueGroup = new NatsKey(opts.Value.QueueGroup);
        }

        if (ConnectionState == NatsConnectionState.Open)
        {
            natsSub.InternalSubscription = _subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(subject, queueGroup, natsSub, cancellationToken);
            return new ValueTask<NatsSub<T>>(natsSub);
        }
        else
        {
            return WithConnectAsync(subject, queueGroup, natsSub, cancellationToken, static (self, key, qg, handler, token) =>
            {
                handler.InternalSubscription = self._subscriptionManager.AddAsync<ReadOnlyMemory<byte>>(key, qg, handler, token);
                return new ValueTask<NatsSub<T>>(handler);
            });
        }
    }
}
