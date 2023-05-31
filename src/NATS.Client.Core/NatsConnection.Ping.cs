using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
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

    private void PostPing(CancellationToken cancellationToken = default)
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
}
