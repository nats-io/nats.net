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
    /// Sends data asynchronously over the connection.
    /// </summary>
    /// <param name="buffer">The buffer containing the data to send.</param>
    /// <returns>A task representing the asynchronous send operation with the number of bytes sent.</returns>
    ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer);

    /// <summary>
    /// Receives data asynchronously from the connection.
    /// </summary>
    /// <param name="buffer">The buffer to store the received data.</param>
    /// <returns>A task representing the asynchronous receive operation with the number of bytes received.</returns>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer);
}

/// <summary>
/// Obsolete, use <see cref="INatsSocketConnection"/> instead
/// </summary>
[Obsolete("This interface is obsolete, use INatsSocketConnection instead.", error: false)]
public interface ISocketConnection : INatsSocketConnection
{
    Task<Exception> WaitForClosed { get; }

    void SignalDisconnected(Exception exception);

    ValueTask AbortConnectionAsync(CancellationToken cancellationToken);
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
