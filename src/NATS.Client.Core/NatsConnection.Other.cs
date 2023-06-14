using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    public IObservable<T> AsObservable<T>(string subject)
    {
        return new NatsObservable<T>(this, subject);
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
}
