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
