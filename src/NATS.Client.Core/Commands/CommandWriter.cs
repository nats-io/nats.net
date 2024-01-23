using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Numerics;
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
    private ISocketConnection? _socketConnection;
    private PipeReader? _pipeReader;
    private PipeWriter? _pipeWriter;
    private bool _disposed;

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
    }

    public void Reset(ISocketConnection? socketConnection)
    {
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: _opts.WriterBufferSize, // flush will block after hitting
            resumeWriterThreshold: _opts.WriterBufferSize / 2,
            useSynchronizationContext: false));

        lock (_lock)
        {
            _pipeWriter?.Complete();
            _pipeReader?.Complete();
            _socketConnection = socketConnection;
            _pipeReader = pipe.Reader;
            _pipeWriter = pipe.Writer;
        }
    }

    public async ValueTask DisposeAsync()
    {
#if NET6_0
        _cts.Cancel();
#else
        await _cts.CancelAsync().ConfigureAwait(false);
#endif

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync().ConfigureAwait(false);
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_pipeWriter != null)
                await _pipeWriter.CompleteAsync().ConfigureAwait(false);
            if (_pipeReader != null)
                await _pipeReader.CompleteAsync().ConfigureAwait(false);

            await _readerLoopTask.ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            PipeWriter bw;
            lock (_lock)
            {
                if (_pipeWriter == null)
                    ThrowOnDisconnected();
                bw = _pipeWriter!;
            }

            _protocolWriter.WriteConnect(bw, connectOpts);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
        }
    }

    public async ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            PipeWriter bw;
            lock (_lock)
            {
                if (_pipeWriter == null)
                    ThrowOnDisconnected();
                bw = _pipeWriter!;
            }

            _enqueuePing(pingCommand);

            _protocolWriter.WritePing(bw);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
        }
    }

    public async ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            PipeWriter bw;
            lock (_lock)
            {
                if (_pipeWriter == null)
                    ThrowOnDisconnected();
                bw = _pipeWriter!;
            }

            _protocolWriter.WritePong(bw);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
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
        Interlocked.Increment(ref _counter.PendingMessages);

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            PipeWriter bw;
            lock (_lock)
            {
                if (_pipeWriter == null)
                    ThrowOnDisconnected();
                bw = _pipeWriter!;
            }

            _protocolWriter.WriteSubscribe(bw, sid, subject, queueGroup, maxMsgs);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
        }
    }

    public async ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            PipeWriter bw;
            lock (_lock)
            {
                if (_pipeWriter == null)
                    ThrowOnDisconnected();
                bw = _pipeWriter!;
            }

            _protocolWriter.WriteUnsubscribe(bw, sid, maxMsgs);
            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOnDisconnected() => throw new NatsException("Connection hasn't been established yet.");

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PublishLockedAsync(string subject, string? replyTo,  NatsPooledBufferWriter<byte> payloadBuffer, NatsPooledBufferWriter<byte>? headersBuffer, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        while (!_channelLock.Writer.TryWrite(1))
        {
            await _channelLock.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var payload = payloadBuffer.WrittenMemory;
            var headers = headersBuffer?.WrittenMemory;

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            PipeWriter bw;
            lock (_lock)
            {
                if (_pipeWriter == null)
                    ThrowOnDisconnected();
                bw = _pipeWriter!;
            }

            _protocolWriter.WritePublish(bw, subject, replyTo, headers, payload);

            payloadBuffer.Reset();
            _pool.Return(payloadBuffer);

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }

            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            while (!_channelLock.Reader.TryRead(out _))
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Decrement(ref _counter.PendingMessages);
        }
    }

    private async Task ReaderLoopAsync()
    {
        // 8520 should fit into 6 packets on 1500 MTU TLS connection or 1 packet on 9000 MTU TLS connection
        // assuming 40 bytes TCP overhead + 40 bytes TLS overhead per packet
        const int maxSendMemoryLength = 8520;
        var sendMemory = new Memory<byte>(new byte[maxSendMemoryLength]);

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
                var buffer = result.Buffer;
                try
                {
                    if (!buffer.IsEmpty)
                    {
                        Memory<byte> memory;
                        var length = (int)buffer.Length;

                        if (length > maxSendMemoryLength)
                        {
                            length = maxSendMemoryLength;
                            buffer = buffer.Slice(0, buffer.GetPosition(length));
                            memory = sendMemory;
                        }
                        else
                        {
                            memory = sendMemory.Slice(0, length);
                        }

                        buffer.CopyTo(memory.Span);

                        try
                        {
                            await connection.SendAsync(memory).ConfigureAwait(false);
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
                    }
                }
                finally
                {
                    pipeReader.AdvanceTo(buffer.End);
                }
            }
            catch (InvalidOperationException)
            {
                // We might still be using the previous pipe reader which might be completed already
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception e)
            {
                _logger.LogError(NatsLogEvents.Buffer, e, "Unexpected error in send buffer reader loop");
            }
        }
    }
}

internal sealed class PriorityCommandWriter : IAsyncDisposable
{
    private int _disposed;

    public PriorityCommandWriter(ObjectPool pool, ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing)
    {
        CommandWriter = new CommandWriter(pool, opts, counter, enqueuePing, overrideCommandTimeout: TimeSpan.MaxValue);
        CommandWriter.Reset(socketConnection);
    }

    public CommandWriter CommandWriter { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            // disposing command writer marks pipe writer as complete
            await CommandWriter.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class NatsPooledBufferWriter<T> : IBufferWriter<T>, IObjectPoolNode<NatsPooledBufferWriter<T>>
{
    private const int DefaultInitialBufferSize = 256;

    private readonly ArrayPool<T> _pool;
    private T[]? _array;
    private int _index;
    private NatsPooledBufferWriter<T>? _next;

    public NatsPooledBufferWriter()
    {
        _pool = ArrayPool<T>.Shared;
        _array = _pool.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

    public ref NatsPooledBufferWriter<T>? NextNode => ref _next;

    /// <summary>
    /// Gets the data written to the underlying buffer so far, as a <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    public ReadOnlyMemory<T> WrittenMemory
    {
        get
        {
            var array = _array;

            if (array is null)
            {
                ThrowObjectDisposedException();
            }

            return array!.AsMemory(0, _index);
        }
    }

    /// <summary>
    /// Gets the data written to the underlying buffer so far, as a <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public ReadOnlySpan<T> WrittenSpan
    {
        get
        {
            var array = _array;

            if (array is null)
            {
                ThrowObjectDisposedException();
            }

            return array!.AsSpan(0, _index);
        }
    }

    /// <summary>
    /// Gets the amount of data written to the underlying buffer so far.
    /// </summary>
    public int WrittenCount
    {
        get => _index;
    }

    /// <inheritdoc/>
    public void Advance(int count)
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        if (count < 0)
        {
            ThrowArgumentOutOfRangeExceptionForNegativeCount();
        }

        if (_index > array!.Length - count)
        {
            ThrowArgumentExceptionForAdvancedTooFar();
        }

        _index += count;
    }

    /// <inheritdoc/>
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckBufferAndEnsureCapacity(sizeHint);

        return _array.AsMemory(_index);
    }

    /// <inheritdoc/>
    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckBufferAndEnsureCapacity(sizeHint);

        return _array.AsSpan(_index);
    }

    public void Reset()
    {
        if (_array != null)
            _pool.Return(_array);
        _array = _pool.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        // See comments in MemoryOwner<T> about this
        if (typeof(T) == typeof(char) &&
            _array is char[] chars)
        {
            return new(chars, 0, _index);
        }

        // Same representation used in Span<T>
        return $"NatsPooledBufferWriter<{typeof(T)}>[{_index}]";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentOutOfRangeExceptionForNegativeCount() => throw new ArgumentOutOfRangeException("count", "The count can't be a negative value.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentOutOfRangeExceptionForNegativeSizeHint() => throw new ArgumentOutOfRangeException("sizeHint", "The size hint can't be a negative value.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowArgumentExceptionForAdvancedTooFar() => throw new ArgumentException("The buffer writer has advanced too far.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowObjectDisposedException() => throw new ObjectDisposedException("The current buffer has already been disposed.");

    /// <summary>
    /// Ensures that <see cref="_array"/> has enough free space to contain a given number of new items.
    /// </summary>
    /// <param name="sizeHint">The minimum number of items to ensure space for in <see cref="_array"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckBufferAndEnsureCapacity(int sizeHint)
    {
        var array = _array;

        if (array is null)
        {
            ThrowObjectDisposedException();
        }

        if (sizeHint < 0)
        {
            ThrowArgumentOutOfRangeExceptionForNegativeSizeHint();
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > array!.Length - _index)
        {
            ResizeBuffer(sizeHint);
        }
    }

    /// <summary>
    /// Resizes <see cref="_array"/> to ensure it can fit the specified number of new items.
    /// </summary>
    /// <param name="sizeHint">The minimum number of items to ensure space for in <see cref="_array"/>.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResizeBuffer(int sizeHint)
    {
        var minimumSize = (uint)_index + (uint)sizeHint;

        // The ArrayPool<T> class has a maximum threshold of 1024 * 1024 for the maximum length of
        // pooled arrays, and once this is exceeded it will just allocate a new array every time
        // of exactly the requested size. In that case, we manually round up the requested size to
        // the nearest power of two, to ensure that repeated consecutive writes when the array in
        // use is bigger than that threshold don't end up causing a resize every single time.
        if (minimumSize > 1024 * 1024)
        {
            minimumSize = BitOperations.RoundUpToPowerOf2(minimumSize);
        }

        _pool.Resize(ref _array, (int)minimumSize);
    }
}
