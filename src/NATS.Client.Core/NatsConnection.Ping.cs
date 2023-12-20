using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public async ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
            await ConnectAsync().ConfigureAwait(false);
        }

        await CommandWriter.PingAsync(cancellationToken).ConfigureAwait(false);
        var pingCommand = new PingCommand();
        EnqueuePing(pingCommand);
        return await pingCommand.TaskCompletionSource.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Send PING command to writers channel waiting on the chanel if necessary.
    /// This is to make sure the PING time window is not missed in case the writer
    /// channel is full with other commands and we will wait to enqueue rather than
    /// just trying which might not happen in time on a busy channel.
    /// </summary>
    /// <param name="cancellationToken">Cancels the Ping command</param>
    /// <returns><see cref="ValueTask"/> representing the asynchronous operation</returns>
    private async ValueTask PingOnlyAsync(CancellationToken cancellationToken = default)
    {

/* Unmerged change from project 'NATS.Client.Core(net8.0)'
Before:
/* Unmerged change from project 'NATS.Client.Core(net8.0)'
After:
/* Unmerged change from project 'NATS.Client.Core(net8.0)'
*/
        /* Unmerged change from project 'NATS.Client.Core(net8.0)'
        Before:
                if (ConnectionState == NatsConnectionState.Open) {
        After:
                if (ConnectionState == NatsConnectionState.Open)
                {
        */
        if (ConnectionState == NatsConnectionState.Open)
        {
            await CommandWriter.PingAsync(cancellationToken).ConfigureAwait(false);
            EnqueuePing(new PingCommand());
        }
    }
}
