using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection : INatsCommand
{
    public void PostPing(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            EnqueueCommandSync(PingCommand.Create(_pool, GetCommandTimer(cancellationToken)));
        }
        else
        {
            WithConnect(cancellationToken, static (self, token) => self.EnqueueCommandSync(PingCommand.Create(self._pool, self.GetCommandTimer(token))));
        }
    }

    public ValueTask PostPingAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return EnqueueCommandAsync(PingCommand.Create(_pool, GetCommandTimer(cancellationToken)));
        }
        else
        {
            return WithConnectAsync(cancellationToken, static (self, token) => self.EnqueueCommandAsync(PingCommand.Create(self._pool, self.GetCommandTimer(token))));
        }
    }

    /// <summary>
    /// Send PING command and await PONG. Return value is similar as Round trip time.
    /// </summary>
    public ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPingCommand.Create(this, _pool, GetCommandTimer(cancellationToken));
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
            return WithConnectAsync(cancellationToken, static (self, token) =>
            {
                var command = AsyncPingCommand.Create(self, self._pool, self.GetCommandTimer(token));
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    public ValueTask PublishAsync<T>(in NatsKey key, T value, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), key, value, Options.Serializer);
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
            return WithConnectAsync(key, value, cancellationToken, static (self, k, v, token) =>
            {
                var command = AsyncPublishCommand<T>.Create(self._pool, self.GetCommandTimer(token), k, v, self.Options.Serializer);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    /// <summary>Publish empty message.</summary>
    public ValueTask PublishAsync(string key, CancellationToken cancellationToken = default)
    {
        return PublishAsync(key, Array.Empty<byte>(), cancellationToken);
    }

    /// <summary>Publish empty message.</summary>
    public ValueTask PublishAsync(in NatsKey key, CancellationToken cancellationToken = default)
    {
        return PublishAsync(key, Array.Empty<byte>(), cancellationToken);
    }

    public ValueTask PublishAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(new NatsKey(key, true), value, cancellationToken);
    }

    public ValueTask PublishAsync(in NatsKey key, byte[] value, CancellationToken cancellationToken = default)
    {
        return PublishAsync(key, new ReadOnlyMemory<byte>(value), cancellationToken);
    }

    public ValueTask PublishAsync(string key, byte[] value, CancellationToken cancellationToken = default)
    {
        return PublishAsync(new NatsKey(key, true), value, cancellationToken);
    }

    public ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(_pool, GetCommandTimer(cancellationToken), key, value);
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
            return WithConnectAsync(key, value, cancellationToken, static (self, k, v, token) =>
            {
                var command = AsyncPublishBytesCommand.Create(self._pool, self.GetCommandTimer(token), k, v);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    public ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
    {
        return PublishAsync(new NatsKey(key, true), value);
    }

    /// <summary>Publish empty message.</summary>
    public void PostPublish(in NatsKey key)
    {
        PostPublish(key, Array.Empty<byte>());
    }

    /// <summary>Publish empty message.</summary>
    public void PostPublish(string key)
    {
        PostPublish(key, Array.Empty<byte>());
    }

    public void PostPublish<T>(in NatsKey key, T value)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), key, value, Options.Serializer);
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(key, value, static (self, k, v) =>
            {
                var command = PublishCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), k, v, self.Options.Serializer);
                self.EnqueueCommandSync(command);
            });
        }
    }

    public void PostPublish<T>(string key, T value)
    {
        PostPublish<T>(new NatsKey(key, true), value);
    }

    public void PostPublish(in NatsKey key, byte[] value)
    {
        PostPublish(key, new ReadOnlyMemory<byte>(value));
    }

    public void PostPublish(string key, byte[] value)
    {
        PostPublish(new NatsKey(key, true), value);
    }

    public void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBytesCommand.Create(_pool, GetCommandTimer(CancellationToken.None), key, value);
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(key, value, static (self, k, v) =>
            {
                var command = PublishBytesCommand.Create(self._pool, self.GetCommandTimer(CancellationToken.None), k, v);
                self.EnqueueCommandSync(command);
            });
        }
    }

    public void PostPublish(string key, ReadOnlyMemory<byte> value)
    {
        PostPublish(new NatsKey(key, true), value);
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBatchCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), values, Options.Serializer);
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
            return WithConnectAsync(values, cancellationToken, static (self, v, token) =>
            {
                var command = AsyncPublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(token), v, self.Options.Serializer);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBatchCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), values, Options.Serializer);
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
            return WithConnectAsync(values, cancellationToken, static (self, values, token) =>
            {
                var command = AsyncPublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(token), values, self.Options.Serializer);
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    public void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBatchCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), values, Options.Serializer);
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(values, static (self, v) =>
            {
                var command = PublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), v, self.Options.Serializer);
                self.EnqueueCommandSync(command);
            });
        }
    }

    public void PostPublishBatch<T>(IEnumerable<(string, T?)> values)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBatchCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), values, Options.Serializer);
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(values, static (self, v) =>
            {
                var command = PublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), v, self.Options.Serializer);
                self.EnqueueCommandSync(command);
            });
        }
    }

    // DirectWrite is not supporting CancellationTimer
    public void PostDirectWrite(string protocol, int repeatCount = 1)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            EnqueueCommandSync(new DirectWriteCommand(protocol, repeatCount));
        }
        else
        {
            WithConnect(protocol, repeatCount, static (self, protocol, repeatCount) =>
            {
                self.EnqueueCommandSync(new DirectWriteCommand(protocol, repeatCount));
            });
        }
    }

    public void PostDirectWrite(byte[] protocol)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            EnqueueCommandSync(new DirectWriteCommand(protocol));
        }
        else
        {
            WithConnect(protocol, static (self, protocol) =>
            {
                self.EnqueueCommandSync(new DirectWriteCommand(protocol));
            });
        }
    }

    public void PostDirectWrite(DirectWriteCommand command)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(command, static (self, command) =>
            {
                self.EnqueueCommandSync(command);
            });
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(NatsKey key, TRequest request, CancellationToken cancellationToken = default)
    {
        var timer = GetRequestCommandTimer(cancellationToken);
        try
        {
            TResponse? response;
            if (ConnectionState == NatsConnectionState.Open)
            {
                response = await _requestResponseManager.AddAsync<TRequest, TResponse>(key, InboxPrefix, request, timer.Token).ConfigureAwait(false);
            }
            else
            {
                response = await WithConnectAsync(key, request, timer.Token, static (self, key, request, token) =>
                {
                    return self._requestResponseManager.AddAsync<TRequest, TResponse>(key, self.InboxPrefix, request, token);
                }).ConfigureAwait(false);
            }

            return response;
        }
        finally
        {
            timer.TryReturn();
        }
    }

    public ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request, CancellationToken cancellationToken = default)
    {
        return RequestAsync<TRequest, TResponse>(new NatsKey(key, true), request, cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default)
    {
        return SubscribeRequestAsync(key.Key, requestHandler, cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddRequestHandlerAsync(key, requestHandler, cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, requestHandler, cancellationToken, static (self, key, requestHandler, token) =>
            {
                return self._subscriptionManager.AddRequestHandlerAsync(key, requestHandler, token);
            });
        }
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default)
    {
        return SubscribeRequestAsync(key.Key, requestHandler, cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddRequestHandlerAsync(key, requestHandler, cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, requestHandler, cancellationToken, static (self, key, requestHandler, token) =>
            {
                return self._subscriptionManager.AddRequestHandlerAsync(key, requestHandler, token);
            });
        }
    }

    public ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler, CancellationToken cancellationToken = default)
    {
        return SubscribeAsync<byte[]>(key, _ => handler(), cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeAsync(string key, Action handler, CancellationToken cancellationToken = default)
    {
        return SubscribeAsync<byte[]>(key, _ => handler(), cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler, CancellationToken cancellationToken = default)
    {
        return SubscribeAsync(key.Key, handler, cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync(key, null, handler, cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, handler, cancellationToken, static (self, key, handler, token) =>
            {
                return self._subscriptionManager.AddAsync(key, null, handler, token);
            });
        }
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
    {
        return SubscribeAsync(key.Key, asyncHandler, cancellationToken);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync<T>(
                key,
                null,
                async x =>
                {
                    try
                    {
                        await asyncHandler(x).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occured during subscribe message.");
                    }
                },
                cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, asyncHandler, cancellationToken, static (self, key, asyncHandler, token) =>
            {
                return self._subscriptionManager.AddAsync<T>(
                    key,
                    null,
                    async x =>
                    {
                        try
                        {
                            await asyncHandler(x).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            self._logger.LogError(ex, "Error occured during subscribe message.");
                        }
                    },
                    token);
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync(key.Key, queueGroup, handler, cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, queueGroup, handler, cancellationToken, static (self, key, queueGroup, handler, token) =>
            {
                return self._subscriptionManager.AddAsync(key.Key, queueGroup, handler, token);
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync(key, new NatsKey(queueGroup, true), handler, cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, queueGroup, handler, cancellationToken, static (self, key, queueGroup, handler, token) =>
            {
                return self._subscriptionManager.AddAsync(key, new NatsKey(queueGroup, true), handler, token);
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync<T>(
                key.Key,
                queueGroup,
                async x =>
                {
                    try
                    {
                        await asyncHandler(x).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occured during subscribe message.");
                    }
                },
                cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, queueGroup, asyncHandler, cancellationToken, static (self, key, queueGroup, asyncHandler, token) =>
            {
                return self._subscriptionManager.AddAsync<T>(
                    key.Key,
                    queueGroup,
                    async x =>
                    {
                        try
                        {
                            await asyncHandler(x).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            self._logger.LogError(ex, "Error occured during subscribe message.");
                        }
                    },
                    token);
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return _subscriptionManager.AddAsync<T>(
                key,
                new NatsKey(queueGroup, true),
                async x =>
                {
                    try
                    {
                        await asyncHandler(x).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occured during subscribe message.");
                    }
                },
                cancellationToken);
        }
        else
        {
            return WithConnectAsync(key, queueGroup, asyncHandler, cancellationToken, static (self, key, queueGroup, asyncHandler, token) =>
            {
                return self._subscriptionManager.AddAsync<T>(
                    key,
                    new NatsKey(queueGroup, true),
                    async x =>
                    {
                        try
                        {
                            await asyncHandler(x).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            self._logger.LogError(ex, "Error occured during subscribe message.");
                        }
                    },
                    token);
            });
        }
    }

    public IObservable<T> AsObservable<T>(string key)
    {
        return AsObservable<T>(new NatsKey(key, true));
    }

    public IObservable<T> AsObservable<T>(in NatsKey key)
    {
        return new NatsObservable<T>(this, key);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncFlushCommand.Create(_pool, GetCommandTimer(cancellationToken));
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
            return WithConnectAsync(cancellationToken, static (self, token) =>
            {
                var command = AsyncFlushCommand.Create(self._pool, self.GetCommandTimer(token));
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    internal void PostDirectWrite(ICommand command)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            EnqueueCommandSync(command);
        }
        else
        {
            WithConnect(command, static (self, command) =>
            {
                self.EnqueueCommandSync(command);
            });
        }
    }
}
