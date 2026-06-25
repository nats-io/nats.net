namespace NATS.Client.Core;

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
