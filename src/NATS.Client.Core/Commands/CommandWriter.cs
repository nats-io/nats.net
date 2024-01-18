using System.Buffers;
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

internal sealed record class QueuedCommand(
    Command command,
    string? subject = default,
    int? sid = default,
    int? maxMsgs = default,
    string? replyTo = default,
    string? queueGroup = default,
    ClientOpts? connectOpts = default,
    NatsBufferWriter<byte>? payload = default,
    NatsBufferWriter<byte>? headers = default);

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

    public CommandWriter(NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _counter = counter;
        _defaultCommandTimeout = overrideCommandTimeout ?? opts.CommandTimeout;
        _enqueuePing = enqueuePing;
        _opts = opts;
        _protocolWriter = new ProtocolWriter(opts.SubjectEncoding);
        var channel = Channel.CreateUnbounded<QueuedCommand>(new UnboundedChannelOptions { SingleReader = true });
        _writer = channel.Writer;
        _reader = channel.Reader;
        _writerLoopTask = Task.Run(WriterLoopAsync);
        _readerLoopTask = Task.Run(ReaderLoopAsync);
    }

    private async Task ReaderLoopAsync()
    {
        try
        {
            while (true)
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
                        var bytes = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                        buffer.CopyTo(bytes);
                        var memory = bytes.AsMemory(0, (int)buffer.Length);
                        // Console.WriteLine($"            memory: {new ReadOnlySequence<byte>(memory).Dump()}");
                        try
                        {
                            await connection.SendAsync(memory).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            connection.SignalDisconnected(e);
                        }

                        ArrayPool<byte>.Shared.Return(bytes);
                    }
                }
                finally
                {
                    pipeReader.AdvanceTo(buffer.End);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($">>> ERROR READER LOOP: {ex}");
        }
        finally
        {
        }
    }

    public void Reset(ISocketConnection socketConnection)
    {
        lock (_lock)
        {
            _socketConnection = socketConnection;
            var pipe = new Pipe();
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
                        cmd.payload!.Dispose();
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

                    await bw.FlushAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
           Console.WriteLine($">>> ERROR WRITER LOOP: {ex}");
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
        var cmd = new QueuedCommand(Command.Connect, connectOpts: connectOpts);
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
        _enqueuePing(pingCommand);
        var cmd = new QueuedCommand(Command.Ping);
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        var cmd = new QueuedCommand(Command.Pong);
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        NatsBufferWriter<byte>? headersBuffer = null;
        if (headers != null)
        {
            headersBuffer = new NatsBufferWriter<byte>();
            new HeaderWriter(_opts.HeaderEncoding).Write(headersBuffer, headers);
        }

        var payloadBuffer = new NatsBufferWriter<byte>();
        if (value != null)
            serializer.Serialize(payloadBuffer, value);

        var cmd = new QueuedCommand(
            Command.Publish,
            subject: subject,
            replyTo: replyTo,
            headers: headersBuffer,
            payload: payloadBuffer);

        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask SubscribeAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        var cmd = new QueuedCommand(Command.Subscribe, sid: sid, subject: subject, queueGroup: queueGroup, maxMsgs: maxMsgs);
        return _writer.WriteAsync(cmd, cancellationToken);
    }

    public ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        var cmd = new QueuedCommand(Command.Unsubscribe, sid: sid, maxMsgs: maxMsgs);
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
