using System.Runtime.CompilerServices;
using NATS.Client.Core.Commands;
#if NETSTANDARD2_0 || NETSTANDARD2_1
using NATS.Client.Core.Internal.NetStandardExtensions;
#endif

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
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
            : default;
}
