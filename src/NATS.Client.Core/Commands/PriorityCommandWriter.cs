using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PriorityCommandWriter : IAsyncDisposable
{
    private int _disposed;

    public PriorityCommandWriter(NatsConnection connection, ObjectPool pool, ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing)
    {
        CommandWriter = new CommandWriter(connection, pool, opts, counter, enqueuePing, overrideCommandTimeout: Timeout.InfiniteTimeSpan);
        CommandWriter.Reset(socketConnection);
    }

    public CommandWriter CommandWriter { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            // disposing command writer marks pipe writer as complete
            await CommandWriter.DisposeAsync().ConfigureAwait(false);
        }
    }
}
