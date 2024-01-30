using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

/// <summary>
/// Sets up a buffer (Pipe), and provides methods to write protocol messages to the buffer
/// When methods complete, they have been queued for sending
/// and further cancellation is not possible
/// </summary>
/// <remarks>
/// These methods are in the hot path, and have all been
/// optimized to eliminate allocations and minimize copying
/// </remarks>
internal sealed class CommandWriter : IAsyncDisposable
{
    private readonly ILogger<CommandWriter> _logger;
    private readonly ObjectPool _pool;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts;
    private readonly ConnectionStatsCounter _counter;
    private readonly TimeSpan _defaultCommandTimeout;
    private readonly Action<PingCommand> _enqueuePing;
    private readonly NatsOpts _opts;
    private readonly ProtocolWriter _protocolWriter;
    private readonly Task _readerLoopTask;
    private readonly HeaderWriter _headerWriter;
    private readonly Channel<int> _channelLock;
    private readonly CancellationTimerPool _ctPool;
    private PipeReader? _pipeReader;
    private PipeWriter? _pipeWriter;
    private ISocketConnection? _socketConnection;
    private volatile bool _disposed;

    public CommandWriter(ObjectPool pool, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _logger = opts.LoggerFactory.CreateLogger<CommandWriter>();
        _pool = pool;
        _counter = counter;
        _defaultCommandTimeout = overrideCommandTimeout ?? opts.CommandTimeout;
        _enqueuePing = enqueuePing;
        _opts = opts;
        _protocolWriter = new ProtocolWriter(opts.SubjectEncoding);
        _channelLock = Channel.CreateBounded<int>(1);
        _headerWriter = new HeaderWriter(_opts.HeaderEncoding);
        _cts = new CancellationTokenSource();
        _readerLoopTask = Task.Run(ReaderLoopAsync);

        // _ctPool = new CancellationTimerPool(_pool, _cts.Token);
        _ctPool = new CancellationTimerPool(_pool, CancellationToken.None);
    }

    public void Reset(ISocketConnection? socketConnection)
    {
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: _opts.WriterBufferSize, // flush will block after hitting
            resumeWriterThreshold: _opts.WriterBufferSize / 2,
            useSynchronizationContext: false));

        lock (_lock)
        {
            _pipeReader?.Complete();
            _pipeWriter?.Complete();
            _pipeReader = pipe.Reader;
            _pipeWriter = pipe.Writer;
            _socketConnection = socketConnection;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

#if NET6_0
        _cts.Cancel();
#else
        await _cts.CancelAsync().ConfigureAwait(false);
#endif

        await _channelLock.Writer.WriteAsync(1).ConfigureAwait(false);
        _channelLock.Writer.TryComplete();

        if (_pipeWriter != null)
            await _pipeWriter.CompleteAsync().ConfigureAwait(false);

        if (_pipeReader != null)
            await _pipeReader.CompleteAsync().ConfigureAwait(false);

        await _readerLoopTask.ConfigureAwait(false);
    }

    public async ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        Interlocked.Increment(ref _counter.PendingMessages);

        try
        {
            await _channelLock.Writer.WriteAsync(1, cancellationTimer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ChannelClosedException)
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var bw = GetWriter();
            _protocolWriter.WriteConnect(bw, connectOpts);
            await bw.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
            cancellationTimer.TryReturn();
        }
    }

    public async ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
        await LockAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _enqueuePing(pingCommand);

            var bw = GetWriter();
            _protocolWriter.WritePing(bw);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        await LockAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var bw = GetWriter();
            _protocolWriter.WritePong(bw);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        NatsPooledBufferWriter<byte>? headersBuffer = null;
        if (headers != null)
        {
            if (!_pool.TryRent(out headersBuffer))
                headersBuffer = new NatsPooledBufferWriter<byte>();
            _headerWriter.Write(headersBuffer, headers);
        }

        NatsPooledBufferWriter<byte> payloadBuffer;
        if (!_pool.TryRent(out payloadBuffer!))
            payloadBuffer = new NatsPooledBufferWriter<byte>();
        if (value != null)
            serializer.Serialize(payloadBuffer, value);

        return PublishLockedAsync(subject, replyTo, payloadBuffer, headersBuffer, cancellationToken);
    }

    public async ValueTask SubscribeAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        await LockAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var bw = GetWriter();
            _protocolWriter.WriteSubscribe(bw, sid, subject, queueGroup, maxMsgs);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        await LockAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var bw = GetWriter();
            _protocolWriter.WriteUnsubscribe(bw, sid, maxMsgs);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOnDisconnected() => throw new NatsException("Connection hasn't been established yet.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PipeWriter GetWriter()
    {
        lock (_lock)
        {
            if (_pipeWriter == null)
                ThrowOnDisconnected();
            return _pipeWriter!;
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PublishLockedAsync(string subject, string? replyTo,  NatsPooledBufferWriter<byte> payloadBuffer, NatsPooledBufferWriter<byte>? headersBuffer, CancellationToken cancellationToken)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        await LockAsync(cancellationTimer.Token).ConfigureAwait(false);

        try
        {
            var payload = payloadBuffer.WrittenMemory;
            var headers = headersBuffer?.WrittenMemory;

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var bw = GetWriter();
            _protocolWriter.WritePublish(bw, subject, replyTo, headers, payload);

            payloadBuffer.Reset();
            _pool.Return(payloadBuffer);

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }

            await bw.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationTimer.Token).ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<int> UnLockAsync(CancellationToken cancellationToken)
    {
        Interlocked.Decrement(ref _counter.PendingMessages);
        return _channelLock.Reader.ReadAsync(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask LockAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);
        return _channelLock.Writer.WriteAsync(1, cancellationToken);
    }

    private async Task ReaderLoopAsync()
    {
        var cancellationToken = _cts.Token;
        while (cancellationToken.IsCancellationRequested == false)
        {
            try
            {
                ISocketConnection? connection;
                PipeReader? pipeReader;
                lock (_lock)
                {
                    connection = _socketConnection;
                    pipeReader = _pipeReader;
                }

                if (connection == null || pipeReader == null)
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (result.IsCanceled)
                {
                    break;
                }

                var buffer = result.Buffer;
                var completed = buffer.Start;
                try
                {
                    if (!buffer.IsEmpty)
                    {
                        var bytes = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                        buffer.CopyTo(bytes);
                        var memory = bytes.AsMemory(0, (int)buffer.Length);

                        try
                        {
                            var sent = await connection.SendAsync(memory).ConfigureAwait(false);
                            completed = buffer.GetPosition(sent);
                        }
                        catch (SocketException e)
                        {
                            connection.SignalDisconnected(e);
                            _logger.LogWarning(NatsLogEvents.TcpSocket, e, "Error while sending data");
                        }
                        catch (Exception e)
                        {
                            connection.SignalDisconnected(e);
                            _logger.LogError(NatsLogEvents.TcpSocket, e, "Unexpected error while sending data");
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bytes);
                        }
                    }
                }
                finally
                {
                    pipeReader.AdvanceTo(completed);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (InvalidOperationException)
            {
                // We might still be using the previous pipe reader which might be completed already
            }
            catch (Exception e)
            {
                _logger.LogError(NatsLogEvents.Buffer, e, "Unexpected error in send buffer reader loop");
            }
        }
    }
}
