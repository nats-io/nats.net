using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

// Aborts the connection when a single socket write does not complete within the timeout,
// e.g. when the peer stopped reading and the TCP send window stays closed. Disposing the
// socket fails the pending write so the send loop exits and the normal reconnect runs.
internal sealed class SocketWriteWatchdog : IDisposable
{
    private const long MaxCheckIntervalTicks = (uint.MaxValue - 1L) * TimeSpan.TicksPerMillisecond;

    private readonly SocketConnectionWrapper _socketConnection;
    private readonly ILogger _logger;
    private readonly long _timeoutTimestampTicks;
    private readonly TimeSpan _timeout;
    private readonly Timer _timer;
    private long _writeStartedTimestamp;
    private int _aborted;

    private SocketWriteWatchdog(SocketConnectionWrapper socketConnection, TimeSpan timeout, ILogger logger)
    {
        _socketConnection = socketConnection;
        _logger = logger;
        _timeout = timeout;
        _timeoutTimestampTicks = ToTimestampTicks(timeout);

        // Checking at half the timeout bounds detection between 1x and 1.5x the timeout.
        var checkIntervalTicks = Math.Max(timeout.Ticks / 2, TimeSpan.TicksPerMillisecond);
        var checkInterval = TimeSpan.FromTicks(Math.Min(checkIntervalTicks, MaxCheckIntervalTicks));
        _timer = new Timer(static state => ((SocketWriteWatchdog)state!).Check(), this, checkInterval, checkInterval);
    }

    public static SocketWriteWatchdog? Create(SocketConnectionWrapper socketConnection, TimeSpan timeout, ILogger logger)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        return new SocketWriteWatchdog(socketConnection, timeout, logger);
    }

    public void WriteStarted() => Interlocked.Exchange(ref _writeStartedTimestamp, Stopwatch.GetTimestamp());

    public void WriteCompleted() => Interlocked.Exchange(ref _writeStartedTimestamp, 0);

    public void Dispose() => _timer.Dispose();

    // Saturates: an out-of-range double-to-long cast is not saturating before .NET 9 (TimeSpan.MaxValue).
    private static long ToTimestampTicks(TimeSpan timeout)
    {
        var ticks = timeout.TotalSeconds * Stopwatch.Frequency;
        return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
    }

    private void Check()
    {
        var started = Interlocked.Read(ref _writeStartedTimestamp);
        if (started == 0 || Stopwatch.GetTimestamp() - started < _timeoutTimestampTicks)
        {
            return;
        }

        if (Interlocked.Exchange(ref _aborted, 1) == 1)
        {
            return;
        }

        _timer.Dispose();
        _ = AbortAsync();
    }

    private async Task AbortAsync()
    {
        try
        {
            _logger.LogWarning(NatsLogEvents.Connection, "Socket write did not complete within {SocketWriteTimeout}. Aborting connection", _timeout);
        }
        catch
        {
            // ignore
        }

        try
        {
            await _socketConnection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            try
            {
                _logger.LogWarning(NatsLogEvents.Connection, e, "Error aborting connection after socket write timeout");
            }
            catch
            {
                // ignore
            }
        }
    }
}
