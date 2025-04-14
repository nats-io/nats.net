namespace NATS.Client.Core;

/// <summary>
/// Factory interface for creating socket connections to NATS servers.
/// </summary>
/// <remarks>
/// This interface abstracts the creation of socket connections to NATS servers,
/// allowing for custom implementations of connection logic.
/// </remarks>
public interface INatsSocketConnectionFactory
{
    /// <summary>
    /// Establishes a connection to a NATS server at the specified URI.
    /// </summary>
    /// <param name="uri">The URI of the NATS server to connect to.</param>
    /// <param name="opts">The Options associated with the NATS Client.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task that represents the asynchronous connect operation. The task result contains
    /// the socket connection.
    /// </returns>
    ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken);
}
