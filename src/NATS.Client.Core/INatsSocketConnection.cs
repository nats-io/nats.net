using System.Net.Sockets;

namespace NATS.Client.Core;

/// <summary>
/// Represents a socket connection to a NATS server.
/// </summary>
/// <remarks>
/// This interface defines the contract for low-level socket connections to NATS servers,
/// providing methods for sending and receiving data, as well as managing the connection lifecycle.
/// </remarks>
public interface INatsSocketConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets a task that completes when the connection is closed, containing the exception that caused the closure.
    /// </summary>
    public Task<Exception> WaitForClosed { get; }

    /// <summary>
    /// Sends data asynchronously over the connection.
    /// </summary>
    /// <param name="buffer">The buffer containing the data to send.</param>
    /// <returns>A task representing the asynchronous send operation with the number of bytes sent.</returns>
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer);

    /// <summary>
    /// Receives data asynchronously from the connection.
    /// </summary>
    /// <param name="buffer">The buffer to store the received data.</param>
    /// <returns>A task representing the asynchronous receive operation with the number of bytes received.</returns>
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer);

    /// <summary>
    /// Aborts the current connection.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous abort operation.</returns>
    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signals that the connection has been disconnected.
    /// </summary>
    /// <param name="exception">The exception that caused the disconnection.</param>
    public void SignalDisconnected(Exception exception);
}

/// <summary>
/// Renamed, use <see cref="INatsSocketConnection"/> instead
/// </summary>
[Obsolete("This interface has been renamed, use INatsSocketConnection instead.", error: false)]
public interface ISocketConnection : INatsSocketConnection
{
}

/// <summary>
/// Represents a NATS socket connection that can be upgraded to use TLS.
/// </summary>
/// <remarks>
/// This interface extends <see cref="INatsSocketConnection"/> to provide access to the underlying
/// socket that is used to create a SslStream.
/// </remarks>
public interface INatsTlsUpgradeableSocketConnection : INatsSocketConnection
{
    /// <summary>
    /// Gets the underlying Socket instance for the connection.
    /// </summary>
    Socket Socket { get; }
}
