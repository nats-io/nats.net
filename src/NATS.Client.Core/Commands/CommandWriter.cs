using System.Buffers;
using System.IO.Pipelines;
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
    private readonly int _arrayPoolInitialSize;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts;
    private readonly ConnectionStatsCounter _counter;
    private readonly TimeSpan _defaultCommandTimeout;
    private readonly Action<PingCommand> _enqueuePing;
    private readonly ProtocolWriter _protocolWriter;
    private readonly HeaderWriter _headerWriter;
    private readonly Channel<int> _channelLock;
    private readonly Channel<int> _channelSize;
    private readonly CancellationTimerPool _ctPool;
    private readonly PipeReader _pipeReader;
    private readonly PipeWriter _pipeWriter;
    private ISocketConnection? _socketConnection;
    private Task? _readerLoopTask;
    private CancellationTokenSource? _ctsReader;
    private volatile bool _disposed;

    public CommandWriter(ObjectPool pool, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _logger = opts.LoggerFactory.CreateLogger<CommandWriter>();
        _pool = pool;

        // Derive ArrayPool rent size from buffer size to
        // avoid defining another option.
        _arrayPoolInitialSize = opts.WriterBufferSize / 256;

        _counter = counter;
        _defaultCommandTimeout = overrideCommandTimeout ?? opts.CommandTimeout;
        _enqueuePing = enqueuePing;
        _protocolWriter = new ProtocolWriter(opts.SubjectEncoding);
        _channelLock = Channel.CreateBounded<int>(1);
        _channelSize = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        _headerWriter = new HeaderWriter(opts.HeaderEncoding);
        _cts = new CancellationTokenSource();

        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: opts.WriterBufferSize, // flush will block after hitting
            resumeWriterThreshold: opts.WriterBufferSize / 2,
            useSynchronizationContext: false));
        _pipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;

        // We need a new ObjectPool here because of the root token (_cts.Token).
        // When the root token is cancelled as this object is disposed, cancellation
        // objects in the pooled CancellationTimer should not be reused since the
        // root token would already be cancelled which means CancellationTimer tokens
        // would always be in a cancelled state.
        _ctPool = new CancellationTimerPool(new ObjectPool(opts.ObjectPoolSize), _cts.Token);
    }

    public void Reset(ISocketConnection socketConnection)
    {
        lock (_lock)
        {
            _socketConnection = socketConnection;
            _ctsReader = new CancellationTokenSource();

            _readerLoopTask = Task.Run(async () =>
            {
                await ReaderLoopAsync(_logger, _socketConnection, _pipeReader, _channelSize, _ctsReader.Token).ConfigureAwait(false);
            });
        }
    }

    public async Task CancelReaderLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? readerTask;
        lock (_lock)
        {
            cts = _ctsReader;
            readerTask = _readerLoopTask;
        }

        if (cts != null)
        {
#if NET6_0
            cts.Cancel();
#else
            await cts.CancelAsync().ConfigureAwait(false);
#endif
        }

        if (readerTask != null)
            await readerTask.WaitAsync(TimeSpan.FromSeconds(3), _cts.Token).ConfigureAwait(false);
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

        _channelLock.Writer.TryComplete();
        _channelSize.Writer.TryComplete();
        await _pipeWriter.CompleteAsync().ConfigureAwait(false);

        Task? readerTask;
        lock (_lock)
        {
            readerTask = _readerLoopTask;
        }

        if (readerTask != null)
            await readerTask.ConfigureAwait(false);
    }

    public async ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        await LockAsync(cancellationTimer.Token).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WriteConnect(_pipeWriter, connectOpts);

            var size = (int)_pipeWriter.UnflushedBytes;
            _channelSize.Writer.TryWrite(size);

            var result = await _pipeWriter.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        finally
        {
            await UnLockAsync().ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    public async ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        await LockAsync(cancellationTimer.Token).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _enqueuePing(pingCommand);

            _protocolWriter.WritePing(_pipeWriter);

            var size = (int)_pipeWriter.UnflushedBytes;
            _channelSize.Writer.TryWrite(size);

            var result = await _pipeWriter.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        finally
        {
            await UnLockAsync().ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    public async ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        await LockAsync(cancellationTimer.Token).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WritePong(_pipeWriter);

            var size = (int)_pipeWriter.UnflushedBytes;
            _channelSize.Writer.TryWrite(size);

            var result = await _pipeWriter.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        finally
        {
            await UnLockAsync().ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        NatsPooledBufferWriter<byte>? headersBuffer = null;
        if (headers != null)
        {
            if (!_pool.TryRent(out headersBuffer))
                headersBuffer = new NatsPooledBufferWriter<byte>(_arrayPoolInitialSize);
            _headerWriter.Write(headersBuffer, headers);
        }

        NatsPooledBufferWriter<byte> payloadBuffer;
        if (!_pool.TryRent(out payloadBuffer!))
            payloadBuffer = new NatsPooledBufferWriter<byte>(_arrayPoolInitialSize);
        if (value != null)
            serializer.Serialize(payloadBuffer, value);

        return PublishLockedAsync(subject, replyTo, payloadBuffer, headersBuffer, cancellationToken);
    }

    public async ValueTask SubscribeAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        await LockAsync(cancellationTimer.Token).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WriteSubscribe(_pipeWriter, sid, subject, queueGroup, maxMsgs);

            var size = (int)_pipeWriter.UnflushedBytes;
            _channelSize.Writer.TryWrite(size);

            var result = await _pipeWriter.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        finally
        {
            await UnLockAsync().ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    public async ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        var cancellationTimer = _ctPool.Start(_defaultCommandTimeout, cancellationToken);
        await LockAsync(cancellationTimer.Token).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WriteUnsubscribe(_pipeWriter, sid, maxMsgs);

            var size = (int)_pipeWriter.UnflushedBytes;
            _channelSize.Writer.TryWrite(size);

            var result = await _pipeWriter.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        finally
        {
            await UnLockAsync().ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    // only used for internal testing
    internal bool TestStallFlush() => _channelLock.Writer.TryWrite(1);

    private static async Task ReaderLoopAsync(ILogger<CommandWriter> logger, ISocketConnection connection, PipeReader pipeReader, Channel<int> channelSize, CancellationToken cancellationToken)
    {
        try
        {
            var examinedOffset = 0;
            while (true)
            {
                var result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (result.IsCanceled)
                {
                    break;
                }

                var buffer = result.Buffer;
                var consumed = buffer.Start;

                buffer = buffer.Slice(examinedOffset);
                var examined = buffer.Start;

                try
                {
                    if (!buffer.IsEmpty)
                    {
                        var bufferLength = (int)buffer.Length;

                        var bytes = ArrayPool<byte>.Shared.Rent(bufferLength);
                        buffer.CopyTo(bytes);
                        var memory = bytes.AsMemory(0, (int)buffer.Length);

                        try
                        {
                            var totalSent = 0;
                            var totalSize = 0;
                            while (totalSent < bufferLength)
                            {
                                var sent = await connection.SendAsync(memory).ConfigureAwait(false);

                                totalSent += sent;
                                memory = memory[sent..];

                                while (totalSize < totalSent)
                                {
                                    int peek;
                                    while (!channelSize.Reader.TryPeek(out peek))
                                    {
                                        await channelSize.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                                    }

                                    // Don't just mark the message as complete if we have more data to send
                                    if (totalSize + peek > totalSent)
                                    {
                                        break;
                                    }

                                    var size = await channelSize.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                                    totalSize += size;
                                    examinedOffset = 0;
                                }

                                // make sure to mark the buffer only at message boundaries.
                                consumed = buffer.GetPosition(totalSize);
                                examined = buffer.GetPosition(totalSent);
                                examinedOffset += totalSent - totalSize;
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bytes);
                        }
                    }
                }
                finally
                {
                    // Always examine to the end to potentially unblock writer
                    pipeReader.AdvanceTo(consumed, examined);
                }

                if (result.IsCompleted)
                {
                    break;
                }
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
            logger.LogError(NatsLogEvents.Buffer, e, "Unexpected error in send buffer reader loop");
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PublishLockedAsync(string subject, string? replyTo, NatsPooledBufferWriter<byte> payloadBuffer, NatsPooledBufferWriter<byte>? headersBuffer, CancellationToken cancellationToken)
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

            _protocolWriter.WritePublish(_pipeWriter, subject, replyTo, headers, payload);

            payloadBuffer.Reset();
            _pool.Return(payloadBuffer);

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }

            var size = (int)_pipeWriter.UnflushedBytes;
            _channelSize.Writer.TryWrite(size);

            var result = await _pipeWriter.FlushAsync(cancellationTimer.Token).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }
        finally
        {
            await UnLockAsync().ConfigureAwait(false);
            cancellationTimer.TryReturn();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask LockAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);
        try
        {
            await _channelLock.Writer.WriteAsync(1, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            throw new OperationCanceledException();
        }
        catch (ChannelClosedException)
        {
            throw new OperationCanceledException();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<int> UnLockAsync()
    {
        Interlocked.Decrement(ref _counter.PendingMessages);
        return _channelLock.Reader.ReadAsync(_cts.Token);
    }
}
