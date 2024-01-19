using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
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
    public NatsBufferWriter<byte>? payload { get; set; }
    public NatsBufferWriter<byte>? headers { get; set; }
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
    private readonly ObjectPool<QueuedCommand> _pool;
    private readonly ObjectPool<NatsBufferWriter<byte>> _pool2;
    private readonly HeaderWriter _headerWriter;

    public CommandWriter(NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _counter = counter;
        _defaultCommandTimeout = overrideCommandTimeout ?? opts.CommandTimeout;
        _enqueuePing = enqueuePing;
        _opts = opts;
        _protocolWriter = new ProtocolWriter(opts.SubjectEncoding);
        var channel = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(128) { SingleReader = true });
        _writer = channel.Writer;
        _reader = channel.Reader;
        _pool = new ObjectPool<QueuedCommand>(() => new QueuedCommand());
        _pool2 = new ObjectPool<NatsBufferWriter<byte>>(() => new NatsBufferWriter<byte>());
        _headerWriter = new HeaderWriter(_opts.HeaderEncoding);
        _writerLoopTask = Task.Run(WriterLoopAsync);
        _readerLoopTask = Task.Run(ReaderLoopAsync);
    }

    private async Task ReaderLoopAsync()
    {
        // 8520 should fit into 6 packets on 1500 MTU TLS connection or 1 packet on 9000 MTU TLS connection
        // assuming 40 bytes TCP overhead + 40 bytes TLS overhead per packet
        var consolidateMem = new Memory<byte>(new byte[8520]);
        try
        {
            while (true)
            {
                try
                {
                    ISocketConnection connection;
                    PipeReader pipeReader;

                    lock (_lock)
                    {
                        connection = _socketConnection;
                        pipeReader = _pipeReader;
                    }

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
                            var length = Math.Min((int)buffer.Length, consolidateMem.Length);
                            // var bytes = ArrayPool<byte>.Shared.Rent(length);
                            var memory = consolidateMem.Slice(0, length);
                            if (length != (int)buffer.Length)
                                buffer = buffer.Slice(0, buffer.GetPosition(length));
                            buffer.CopyTo(memory.Span);
                            //var memory = bytes.AsMemory(0, length);
                            // Console.WriteLine($"            memory: {new ReadOnlySequence<byte>(memory).Dump()}");
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($">>> ERROR READER OUTER LOOP: {ex}");
        }
        finally
        {
        }
    }

    public void Reset(ISocketConnection socketConnection)
    {
        lock (_lock)
        {
            _pipeWriter?.Complete();
            _pipeReader?.Complete();
            _socketConnection = socketConnection;
            var pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
            _pipeReader = pipe.Reader;
            _pipeWriter = pipe.Writer;
        }
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (await _reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_reader.TryRead(out var cmd))
                {
                    // Console.WriteLine($">>> COMMAND: {cmd.command}");
                    try
                    {
                        PipeWriter bw;
                        lock (_lock)
                        {
                            bw = _pipeWriter;
                        }

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
                            cmd.headers?.Dispose();
                            cmd.headers?.Reset();
                            cmd.payload!.Reset();
                            if (cmd.headers != null)
                                _pool2.Return(cmd.headers);
                            _pool2.Return(cmd.payload);
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
                        await bw.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($">>> ERROR WRITER LOOP: {e}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
           Console.WriteLine($">>> ERROR WRITER OUTER LOOP: {ex}");
        }
        finally
        {
        }
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

    public ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
        _enqueuePing(pingCommand);
        var cmd = _pool.Get();
        cmd.command = Command.Ping;
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        var cmd = _pool.Get();
        cmd.command = Command.Pong;
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        NatsBufferWriter<byte>? headersBuffer = null;
        if (headers != null)
        {
            headersBuffer = _pool2.Get();
            _headerWriter.Write(headersBuffer, headers);
        }

        var payloadBuffer = _pool2.Get();
        if (value != null)
            serializer.Serialize(payloadBuffer, value);

        var cmd = _pool.Get();
        cmd.command = Command.Publish;
        cmd.subject = subject;
        cmd.replyTo = replyTo;
        cmd.headers = headersBuffer;
        cmd.payload = payloadBuffer;

        return _writer.WriteAsync(cmd, cancellationToken);
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

    public PriorityCommandWriter(ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing)
    {
        CommandWriter = new CommandWriter(opts, counter, enqueuePing, overrideCommandTimeout: TimeSpan.MaxValue);
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

public class ObjectPool<T>
{
    private readonly ConcurrentBag<T> _objects;
    private readonly Func<T> _objectGenerator;

    public ObjectPool(Func<T> objectGenerator)
    {
        _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        _objects = new ConcurrentBag<T>();
    }

    public T Get() => _objects.TryTake(out T item) ? item : _objectGenerator();

    public void Return(T item) => _objects.Add(item);
}
