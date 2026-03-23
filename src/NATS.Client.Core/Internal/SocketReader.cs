using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

// When socket is closed/disposed, operation throws SocketClosedException
internal sealed class SocketReader
{
    private readonly int _minimumBufferSize;
    private readonly int _maxControlLineSize;
    private readonly ConnectionStatsCounter _counter;
    private readonly SeqeunceBuilder _seqeunceBuilder = new SeqeunceBuilder();
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly ILogger<SocketReader> _logger;
    private readonly bool _isTraceLogging;
    private readonly SocketConnectionWrapper _socketConnection;

    private Memory<byte> _availableMemory;

    public SocketReader(SocketConnectionWrapper socketConnection, int minimumBufferSize, ConnectionStatsCounter counter, ILoggerFactory loggerFactory, int maxControlLineSize = 4 * 1024 * 1024)
    {
        _socketConnection = socketConnection;
        _minimumBufferSize = minimumBufferSize;
        _maxControlLineSize = maxControlLineSize;
        _counter = counter;
        _logger = loggerFactory.CreateLogger<SocketReader>();
        _isTraceLogging = _logger.IsEnabled(LogLevel.Trace);
    }

#if !NETSTANDARD
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(int minimumSize)
    {
        var totalRead = 0;
        do
        {
            if (_availableMemory.Length == 0)
            {
                _availableMemory = ArrayPool<byte>.Shared.Rent(_minimumBufferSize);
            }

            _stopwatch.Restart();
            int read;
            try
            {
                read = await _socketConnection.ReceiveAsync(_availableMemory).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _socketConnection.SignalDisconnected(ex);
                throw new SocketClosedException(ex);
            }

            _stopwatch.Stop();
            if (_isTraceLogging)
            {
                _logger.LogTrace(NatsLogEvents.TcpSocket, "Socket.ReceiveAsync Size: {Size} Elapsed: {ElapsedMs}ms", read, _stopwatch.Elapsed.TotalMilliseconds);
            }

            if (read == 0)
            {
                var ex = new SocketClosedException(null);
                _socketConnection.SignalDisconnected(ex);
                throw ex;
            }

            totalRead += read;
            Interlocked.Add(ref _counter.ReceivedBytes, read);
            _seqeunceBuilder.Append(_availableMemory.Slice(0, read));
            _availableMemory = _availableMemory.Slice(read);
        }
        while (totalRead < minimumSize);

        return _seqeunceBuilder.ToReadOnlySequence();
    }

#if !NETSTANDARD
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<ReadOnlySequence<byte>> ReadUntilReceiveNewLineAsync()
    {
        var totalRead = 0;
        while (true)
        {
            if (_availableMemory.Length == 0)
            {
                _availableMemory = ArrayPool<byte>.Shared.Rent(_minimumBufferSize);
            }

            _stopwatch.Restart();
            int read;
            try
            {
                read = await _socketConnection.ReceiveAsync(_availableMemory).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _socketConnection.SignalDisconnected(ex);
                throw new SocketClosedException(ex);
            }

            _stopwatch.Stop();
            if (_isTraceLogging)
            {
                _logger.LogTrace(NatsLogEvents.TcpSocket, "Socket.ReceiveAsync Size: {Size} Elapsed: {ElapsedMs}ms", read, _stopwatch.Elapsed.TotalMilliseconds);
            }

            if (read == 0)
            {
                var ex = new SocketClosedException(null);
                _socketConnection.SignalDisconnected(ex);
                throw ex;
            }

            totalRead += read;
            Interlocked.Add(ref _counter.ReceivedBytes, read);
            var appendMemory = _availableMemory.Slice(0, read);
            _seqeunceBuilder.Append(appendMemory);
            _availableMemory = _availableMemory.Slice(read);

            if (appendMemory.Span.Contains((byte)'\n'))
            {
                break;
            }

            if (totalRead > _maxControlLineSize)
            {
                var msg = $"Control line exceeded maximum size of {_maxControlLineSize} bytes without newline";
                _socketConnection.SignalDisconnected(new NatsProtocolViolationException(msg));
                NatsProtocolViolationException.Throw(msg);
            }
        }

        return _seqeunceBuilder.ToReadOnlySequence();
    }

    public void AdvanceTo(SequencePosition start)
    {
        _seqeunceBuilder.AdvanceTo(start);
    }
}
