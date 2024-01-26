using System.Runtime.CompilerServices;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        PingCommand pingCommand;
        if (!_pool.TryRent(out pingCommand!))
        {
            pingCommand = new PingCommand(_pool);
        }

        pingCommand.Start();

        await CommandWriter.PingAsync(pingCommand, cancellationToken).ConfigureAwait(false);

        return await pingCommand.RunAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Send PING command to writers channel waiting on the chanel if necessary.
    /// This is to make sure the PING time window is not missed in case the writer
    /// channel is full with other commands and we will wait to enqueue rather than
    /// just trying which might not happen in time on a busy channel.
    /// </summary>
    /// <param name="cancellationToken">Cancels the Ping command</param>
    /// <returns><see cref="ValueTask"/> representing the asynchronous operation</returns>
    private ValueTask PingOnlyAsync(CancellationToken cancellationToken = default) =>
        ConnectionState == NatsConnectionState.Open
            ? CommandWriter.PingAsync(new PingCommand(_pool), cancellationToken)
            : ValueTask.CompletedTask;
}
