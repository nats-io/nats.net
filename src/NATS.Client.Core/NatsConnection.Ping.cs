using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPingCommand.Create(this, _pool, GetCancellationTimer(cancellationToken));
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
                var command = AsyncPingCommand.Create(self, self._pool, self.GetCancellationTimer(token));
                return self.EnqueueAndAwaitCommandAsync(command);
            });
        }
    }

    /// <summary>
    /// Send PING command to writers channel waiting on the chanel if necessary.
    /// This is to make sure the PING time window is not missed in case the writer
    /// channel is full with other commands and we will wait to enqueue rather than
    /// just trying which might not happen in time on a busy channel.
    /// </summary>
    /// <param name="cancellationToken">Cancels the Ping command</param>
    /// <returns><see cref="ValueTask"/> representing the asynchronous operation</returns>
    private ValueTask PingOnlyAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return EnqueueCommandAsync(PingCommand.Create(_pool, GetCancellationTimer(cancellationToken)));
        }

        return ValueTask.CompletedTask;
    }
}
