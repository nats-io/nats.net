using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public partial class NatsConnection : INatsCommand
{
    internal async ValueTask EnqueueAndAwaitCommandAsync(IAsyncCommand command)
    {
        await EnqueueCommandAsync(command).ConfigureAwait(false);
        await command.AsValueTask().ConfigureAwait(false);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private CancellationTimer GetCommandTimer(CancellationToken cancellationToken)
    {
        return _cancellationTimerPool.Start(Options.CommandTimeout, cancellationToken);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool TryEnqueueCommand(ICommand command)
    {
        if (_commandWriter.TryWrite(command))
        {
            Interlocked.Increment(ref Counter.PendingMessages);
            return true;
        }
        else
        {
            return false;
        }
    }
}
