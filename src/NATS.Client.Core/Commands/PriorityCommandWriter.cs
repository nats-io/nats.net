using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class PriorityCommandWriter : IAsyncDisposable
{
    private int _disposed;

    public PriorityCommandWriter(ObjectPool pool, ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing)
    {
        CommandWriter = new CommandWriter(pool, opts, counter, enqueuePing, overrideCommandTimeout: TimeSpan.MaxValue);
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
