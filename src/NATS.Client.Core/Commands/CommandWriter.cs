using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal enum Command
{
    Connect,
    Ping,
    Pong,
    Publish,
    Subscribe,
    Unsubscribe,
}

internal sealed class QueuedCommand
{
    public Command command { get; set; }
    public string? subject { get; set; }
    public int? sid { get; set; }
    public int? maxMsgs { get; set; }
    public string? replyTo { get; set; }
    public string? queueGroup { get; set; }
    public ClientOpts? connectOpts { get; set; }
    public NatsPooledBufferWriter<byte>? payload { get; set; }
    public NatsPooledBufferWriter<byte>? headers { get; set; }
}

/// <summary>
/// Sets up a Pipe, and provides methods to write to the PipeWriter
/// When methods complete, they have been queued for sending
/// and further cancellation is not possible
/// </summary>
/// <remarks>
/// These methods are in the hot path, and have all been
/// optimized to eliminate allocations and make an initial attempt
/// to run synchronously without the async state machine
/// </remarks>
internal sealed class CommandWriter : IAsyncDisposable
{
    private readonly SemaphoreSlim _semLock;

    private readonly ConnectionStatsCounter _counter;
    private readonly TimeSpan _defaultCommandTimeout;
    private readonly Action<PingCommand> _enqueuePing;
    private readonly NatsOpts _opts;
    private readonly ChannelWriter<QueuedCommand> _writer;
    private readonly ChannelReader<QueuedCommand> _reader;
    private readonly ProtocolWriter _protocolWriter;
    private readonly Task _writerLoopTask;
    private ISocketConnection _socketConnection;
    private PipeReader _pipeReader;
    private PipeWriter _pipeWriter;
    private readonly object _lock = new();
    private readonly Task _readerLoopTask;
    private readonly ObjectPool2<QueuedCommand> _pool;
    private readonly ObjectPool _pool0;
    // private readonly ObjectPool2<NatsPooledBufferWriter<byte>> _pool2;
    private readonly HeaderWriter _headerWriter;
    private readonly Channel<int> _chan;

    public CommandWriter(ObjectPool pool, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _semLock = new SemaphoreSlim(1);
        _chan = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        _counter = counter;
        _defaultCommandTimeout = overrideCommandTimeout ?? opts.CommandTimeout;
        _enqueuePing = enqueuePing;
        _opts = opts;
        _protocolWriter = new ProtocolWriter(opts.SubjectEncoding);
        var capacity = 512;
        // var capacity = _opts.WriterBufferSize / 128;
        var channel = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(capacity) { SingleReader = true });
        _writer = channel.Writer;
        _reader = channel.Reader;
        _pool0 = pool;//new ObjectPool(capacity * 2);
        // _pool2 = new ObjectPool2<NatsPooledBufferWriter<byte>>(() => new NatsPooledBufferWriter<byte>(), capacity * 2);
        _pool = new ObjectPool2<QueuedCommand>(() => new QueuedCommand(), capacity);
        _headerWriter = new HeaderWriter(_opts.HeaderEncoding);
        _writerLoopTask = Task.Run(WriterLoopAsync);
        _readerLoopTask = Task.Run(ReaderLoopAsync);
    }

    private async Task ReaderLoopAsync()
    {
        // 8520 should fit into 6 packets on 1500 MTU TLS connection or 1 packet on 9000 MTU TLS connection
        // assuming 40 bytes TCP overhead + 40 bytes TLS overhead per packet
        var consolidateMemLength = 8520;
        var consolidateMem = new Memory<byte>(new byte[consolidateMemLength]);
        // try
        // {
            while (true)
            {
                try
                {
                    var connection = _socketConnection;
                    var pipeReader = _pipeReader;

                    if (connection == null || pipeReader == null)
                    {
                        await Task.Delay(10).ConfigureAwait(false);
                        continue;
                    }

                    var result = await pipeReader.ReadAsync().ConfigureAwait(false);
                    var buffer = result.Buffer;
                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            // Console.WriteLine($">>> READER: {buffer.Length}");

                            var bufferLength = (int)buffer.Length;
                            var length = Math.Min(bufferLength, consolidateMemLength);
                            var memory = consolidateMem.Slice(0, length);
                            if (length != bufferLength)
                                buffer = buffer.Slice(0, buffer.GetPosition(length));
                            buffer.CopyTo(memory.Span);

                            // var length = (int)buffer.Length;
                            // var bytes = ArrayPool<byte>.Shared.Rent(length);
                            // var memory = bytes.AsMemory().Slice(0, length);
                            // buffer.CopyTo(memory.Span);

                            try
                            {
                                await connection.SendAsync(memory).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                connection.SignalDisconnected(e);
                            }

                            // ArrayPool<byte>.Shared.Return(bytes);
                        }
                    }
                    finally
                    {
                        pipeReader.AdvanceTo(buffer.End);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($">>> ERROR READER LOOP: {e}");
                }
            }
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($">>> ERROR READER OUTER LOOP: {ex}");
        // }
    }

    public void Reset(ISocketConnection socketConnection)
    {
        var pipe = new Pipe(new PipeOptions(
        pauseWriterThreshold: _opts.WriterBufferSize, // flush will block after hitting
        resumeWriterThreshold: _opts.WriterBufferSize / 2,
        // minimumSegmentSize: 16384,
        useSynchronizationContext: false
        ));

        // var pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));

        lock (_lock)
        {
            _pipeWriter?.Complete();
            _pipeReader?.Complete();
            _socketConnection = socketConnection;
            _pipeReader = pipe.Reader;
            _pipeWriter = pipe.Writer;
        }
    }

    private async Task WriterLoopAsync()
    {
        // try
        // {
            while (await _reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_reader.TryRead(out var cmd))
                {
                    await _semLock.WaitAsync().ConfigureAwait(false);
                    // await _chan.Writer.WriteAsync(1).ConfigureAwait(false);
                    // Console.WriteLine($">>> COMMAND: {cmd.command}");
                    try
                    {
                        var bw = _pipeWriter;

                        if (cmd.command == Command.Connect)
                        {
                            _protocolWriter.WriteConnect(bw, cmd.connectOpts!);
                        }
                        else if (cmd.command == Command.Ping)
                        {
                            _protocolWriter.WritePing(bw);
                        }
                        else if (cmd.command == Command.Pong)
                        {
                            _protocolWriter.WritePong(bw);
                        }
                        else if (cmd.command == Command.Publish)
                        {
                            var payload = cmd.payload!.WrittenMemory;
                            var headers = cmd.headers?.WrittenMemory;
                            _protocolWriter.WritePublish(bw, cmd.subject!, cmd.replyTo, headers, payload);
                            cmd.headers?.Reset();
                            cmd.payload!.Reset();
                            if (cmd.headers != null)
                                _pool0.Return(cmd.headers);
                                // _pool2.Return(cmd.headers);
                            _pool0.Return(cmd.payload);
                            // _pool2.Return(cmd.payload);
                        }
                        else if (cmd.command == Command.Subscribe)
                        {
                            _protocolWriter.WriteSubscribe(bw, cmd.sid!.Value, cmd.subject!, cmd.queueGroup, cmd.maxMsgs);
                        }
                        else if (cmd.command == Command.Unsubscribe)
                        {
                            _protocolWriter.WriteUnsubscribe(bw, cmd.sid!.Value, cmd.maxMsgs);
                        }
                        else
                        {
                            throw new ArgumentOutOfRangeException(nameof(cmd.command));
                        }

                        _pool.Return(cmd);
                        var flushResult = await bw.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($">>> ERROR WRITER LOOP: {e}");
                    }
                    finally
                    {
                        // await _chan.Reader.ReadAsync().ConfigureAwait(true);
                        _semLock.Release();
                    }
                }
            }
        // }
        // catch (Exception ex)
        // {
        //    Console.WriteLine($">>> ERROR WRITER OUTER LOOP: {ex}");
        // }
    }

    public ValueTask DisposeAsync()
    {
        _writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    // public NatsPipeliningWriteProtocolProcessor CreateNatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection) => new(socketConnection, this, _opts, _counter);

    public ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        var cmd = _pool.Get();
        cmd.command = Command.Connect;
        cmd.connectOpts = connectOpts;
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public async ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
        _enqueuePing(pingCommand);
        await _semLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var bw = _pipeWriter;
            _protocolWriter.WritePing(bw);
            await bw.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            // await _chan.Reader.ReadAsync().ConfigureAwait(true);
            _semLock.Release();
        }
    }

    public async ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        // var cmd = _pool.Get();
        // cmd.command = Command.Pong;
        // return _writer.WriteAsync(cmd, cancellationToken);

        await _semLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var bw = _pipeWriter;
            _protocolWriter.WritePong(bw);
            await bw.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            // await _chan.Reader.ReadAsync().ConfigureAwait(true);
            _semLock.Release();
        }
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        NatsPooledBufferWriter<byte>? headersBuffer = null;
        if (headers != null)
        {
            if (!_pool0.TryRent(out headersBuffer))
                headersBuffer = new NatsPooledBufferWriter<byte>();
            // headersBuffer = _pool2.Get();
            _headerWriter.Write(headersBuffer, headers);
        }

        NatsPooledBufferWriter<byte> payloadBuffer;
        if (!_pool0.TryRent(out payloadBuffer!))
            payloadBuffer = new NatsPooledBufferWriter<byte>();
        // var payloadBuffer = _pool2.Get();
        if (value != null)
            serializer.Serialize(payloadBuffer, value);

        return PublishLockedAsync(subject, replyTo, payloadBuffer, headersBuffer, cancellationToken);

        // var cmd = _pool.Get();
        // cmd.command = Command.Publish;
        // cmd.subject = subject;
        // cmd.replyTo = replyTo;
        // cmd.headers = headersBuffer;
        // cmd.payload = payloadBuffer;
        // return _writer.WriteAsync(cmd, cancellationToken);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PublishLockedAsync(string subject, string? replyTo,  NatsPooledBufferWriter<byte> payloadBuffer, NatsPooledBufferWriter<byte>? headersBuffer, CancellationToken cancellationToken)
    {
        // await _chan.Writer.WriteAsync(1).ConfigureAwait(false);
        await _semLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var payload = payloadBuffer!.WrittenMemory;
            var headers2 = headersBuffer?.WrittenMemory;

            var bw = _pipeWriter;

            // if (value is byte[] bytes)
            // {
            //     var headers2 = headersBuffer?.WrittenMemory;
            //     _protocolWriter.WritePublish(bw, subject!, replyTo, headers2, bytes);
            //     headersBuffer?.Reset();
            //     if (headersBuffer != null)
            //         _pool2.Return(headersBuffer);
            // }
            // else
            {
                _protocolWriter.WritePublish(bw, subject!, replyTo, headers2, payload);
                headersBuffer?.Reset();
                payloadBuffer!.Reset();
                if (headersBuffer != null)
                    _pool0.Return(headersBuffer);
                    // _pool2.Return(headersBuffer);
                _pool0.Return(payloadBuffer);
                // _pool2.Return(payloadBuffer);
            }

            await bw.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // await _chan.Reader.ReadAsync().ConfigureAwait(true);
            _semLock.Release();
        }
    }

    public ValueTask SubscribeAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        var cmd = _pool.Get();
        cmd.command = Command.Subscribe;
        cmd.sid = sid;
        cmd.subject = subject;
        cmd.queueGroup = queueGroup;
        cmd.maxMsgs = maxMsgs;
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        var cmd = _pool.Get();
        cmd.command = Command.Unsubscribe;
        cmd.sid = sid;
        cmd.maxMsgs = maxMsgs;
        return _writer.WriteAsync(cmd, cancellationToken);
    }
}

internal sealed class PriorityCommandWriter : IAsyncDisposable
{
    // private readonly NatsPipeliningWriteProtocolProcessor _natsPipeliningWriteProtocolProcessor;
    private int _disposed;

    public PriorityCommandWriter(ObjectPool pool, ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing)
    {
        CommandWriter = new CommandWriter(pool, opts, counter, enqueuePing, overrideCommandTimeout: TimeSpan.MaxValue);
        CommandWriter.Reset(socketConnection);
        // _natsPipeliningWriteProtocolProcessor = CommandWriter.CreateNatsPipeliningWriteProtocolProcessor(socketConnection);
    }

    public CommandWriter CommandWriter { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            // disposing command writer marks pipe writer as complete
            await CommandWriter.DisposeAsync().ConfigureAwait(false);
            try
            {
                // write loop will complete once pipe reader completes
                // await _natsPipeliningWriteProtocolProcessor.WriteLoop.ConfigureAwait(false);
            }
            finally
            {
                // await _natsPipeliningWriteProtocolProcessor.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

internal sealed class ObjectPool2<T> where T : class
{
    // https://github.com/dotnet/runtime/blob/2939fde09e594070205f8bda036485c7b398241c/src/libraries/System.Reflection.Metadata/src/System/Reflection/Internal/Utilities/ObjectPool%601.cs
    private struct Element
    {
        internal T? Value;
    }

    // storage for the pool objects.
    private readonly Element[] _items;

    // factory is stored for the lifetime of the pool. We will call this only when pool needs to
    // expand. compared to "new T()", Func gives more flexibility to implementers and faster
    // than "new T()".
    private readonly Func<T> _factory;


    internal ObjectPool2(Func<T> factory)
        : this(factory, Environment.ProcessorCount * 2)
    { }

    internal ObjectPool2(Func<T> factory, int size)
    {
        _factory = factory;
        _items = new Element[size];
    }

    private T CreateInstance()
    {
        var inst = _factory();
        return inst;
    }

    /// <summary>
    /// Produces an instance.
    /// </summary>
    /// <remarks>
    /// Search strategy is a simple linear probing which is chosen for it cache-friendliness.
    /// Note that Free will try to store recycled objects close to the start thus statistically
    /// reducing how far we will typically search.
    /// </remarks>
    internal T Get()
    {
        var items = _items;
        T? inst;

        for (int i = 0; i < items.Length; i++)
        {
            // Note that the read is optimistically not synchronized. That is intentional.
            // We will interlock only when we have a candidate. in a worst case we may miss some
            // recently returned objects. Not a big deal.
            inst = items[i].Value;
            if (inst != null)
            {
                if (inst == Interlocked.CompareExchange(ref items[i].Value, null, inst))
                {
                    goto gotInstance;
                }
            }
        }

        inst = CreateInstance();
    gotInstance:

        return inst;
    }

    /// <summary>
    /// Returns objects to the pool.
    /// </summary>
    /// <remarks>
    /// Search strategy is a simple linear probing which is chosen for it cache-friendliness.
    /// Note that Free will try to store recycled objects close to the start thus statistically
    /// reducing how far we will typically search in Allocate.
    /// </remarks>
    internal void Return(T obj)
    {
        var items = _items;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Value == null)
            {
                // Intentionally not using interlocked here.
                // In a worst case scenario two objects may be stored into same slot.
                // It is very unlikely to happen and will only mean that one of the objects will get collected.
                items[i].Value = obj;
                break;
            }
        }
    }
}

internal sealed class NatsPooledBufferWriter<T> : IBufferWriter<T>, IObjectPoolNode<NatsPooledBufferWriter<T>>
{
    private NatsPooledBufferWriter<T>? _next;

    public ref NatsPooledBufferWriter<T>? NextNode => ref _next;

    private const int DefaultInitialBufferSize = 256;
    private readonly ArrayPool<T> _pool;
    private T[]? _array;
    private int _index;

    public NatsPooledBufferWriter()
    {
        _pool = ArrayPool<T>.Shared;
        _array = _pool.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

    public void Reset()
    {
        if (_array != null)
            _pool.Return(_array);
        _array = _pool.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

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

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the requested count is negative.
    /// </summary>
    private static void ThrowArgumentOutOfRangeExceptionForNegativeCount()
    {
        throw new ArgumentOutOfRangeException("count", "The count can't be a negative value.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the size hint is negative.
    /// </summary>
    private static void ThrowArgumentOutOfRangeExceptionForNegativeSizeHint()
    {
        throw new ArgumentOutOfRangeException("sizeHint", "The size hint can't be a negative value.");
    }

    /// <summary>
    /// Throws an <see cref="ArgumentOutOfRangeException"/> when the requested count is negative.
    /// </summary>
    private static void ThrowArgumentExceptionForAdvancedTooFar()
    {
        throw new ArgumentException("The buffer writer has advanced too far.");
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> when <see cref="_array"/> is <see langword="null"/>.
    /// </summary>
    private static void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException("The current buffer has already been disposed.");
    }

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

        Resize(_pool, ref _array, (int)minimumSize);
    }

    /// <summary>
    /// Changes the number of elements of a rented one-dimensional array to the specified new size.
    /// </summary>
    /// <typeparam name="T">The type of items into the target array to resize.</typeparam>
    /// <param name="pool">The target <see cref="ArrayPool{T}"/> instance to use to resize the array.</param>
    /// <param name="array">The rented <typeparamref name="T"/> array to resize, or <see langword="null"/> to create a new array.</param>
    /// <param name="newSize">The size of the new array.</param>
    /// <param name="clearArray">Indicates whether the contents of the array should be cleared before reuse.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="newSize"/> is less than 0.</exception>
    /// <remarks>When this method returns, the caller must not use any references to the old array anymore.</remarks>
    private static void Resize<T>(ArrayPool<T> pool, [NotNull] ref T[]? array, int newSize, bool clearArray = false)
    {
        // If the old array is null, just create a new one with the requested size
        if (array is null)
        {
            array = pool.Rent(newSize);

            return;
        }

        // If the new size is the same as the current size, do nothing
        if (array.Length == newSize)
        {
            return;
        }

        // Rent a new array with the specified size, and copy as many items from the current array
        // as possible to the new array. This mirrors the behavior of the Array.Resize API from
        // the BCL: if the new size is greater than the length of the current array, copy all the
        // items from the original array into the new one. Otherwise, copy as many items as possible,
        // until the new array is completely filled, and ignore the remaining items in the first array.
        var newArray = pool.Rent(newSize);
        var itemsToCopy = Math.Min(array.Length, newSize);

        Array.Copy(array, 0, newArray, 0, itemsToCopy);

        pool.Return(array, clearArray);

        array = newArray;
    }
}
