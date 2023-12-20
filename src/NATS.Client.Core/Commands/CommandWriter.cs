using System.IO.Pipelines;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal interface ICommandWriter
{
    public ValueTask WriteCommandAsync(Action<ProtocolWriter> commandFunc, CancellationToken cancellationToken);
}

// QueuedCommand is used to track commands that have been queued but not sent
internal readonly record struct QueuedCommand(int Size)
{
}

internal sealed class CommandWriter : ICommandWriter, IAsyncDisposable
{
    private readonly ConnectionStatsCounter _counter;
    private readonly NatsOpts _opts;
    private readonly PipeWriter _pipeWriter;
    private readonly ProtocolWriter _protocolWriter;
    private readonly ChannelWriter<QueuedCommand> _queuedCommandsWriter;
    private readonly SemaphoreSlim _sem = new SemaphoreSlim(1);
    private bool _disposed;

    public CommandWriter(NatsOpts opts, ConnectionStatsCounter counter)
    {
        _counter = counter;
        _opts = opts;
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: opts.WriterBufferSize, resumeWriterThreshold: opts.WriterBufferSize / 2, minimumSegmentSize: 16384));
        PipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;
        _protocolWriter = new ProtocolWriter(_pipeWriter, opts.HeaderEncoding);
        var channel = Channel.CreateUnbounded<QueuedCommand>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        QueuedCommandsReader = channel.Reader;
        _queuedCommandsWriter = channel.Writer;
    }

    public PipeReader PipeReader { get; }

    public ChannelReader<QueuedCommand> QueuedCommandsReader { get; }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _sem.WaitAsync().ConfigureAwait(false);
            _disposed = true;
            _queuedCommandsWriter.Complete();
            await _pipeWriter.CompleteAsync().ConfigureAwait(false);
        }
    }

    public NatsPipeliningWriteProtocolProcessor CreateNatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection) => new(socketConnection, this, _opts, _counter);

    public async ValueTask WriteCommandAsync(Action<ProtocolWriter> commandFunc, CancellationToken cancellationToken = default)
    {
        await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // call commandFunc
            commandFunc(_protocolWriter);
            Interlocked.Add(ref _counter.PendingMessages, 1);

            // write size to queued command channel
            // this must complete before flushing
            var size = (int)_pipeWriter.UnflushedBytes;
            if (!_queuedCommandsWriter.TryWrite(new QueuedCommand(Size: size)))
            {
                throw new NatsException("channel write outside of lock");
            }

            // flush writer
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }
}

internal sealed class PriorityCommandWriter : ICommandWriter, IAsyncDisposable
{
    private readonly CommandWriter _commandWriter;
    private readonly NatsPipeliningWriteProtocolProcessor _natsPipeliningWriteProtocolProcessor;
    private int _disposed;

    public PriorityCommandWriter(ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter)
    {
        _commandWriter = new CommandWriter(opts, counter);
        _natsPipeliningWriteProtocolProcessor = _commandWriter.CreateNatsPipeliningWriteProtocolProcessor(socketConnection);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            // disposing command writer marks pipe writer as complete
            await _commandWriter.DisposeAsync().ConfigureAwait(false);
            try
            {
                // write loop will complete once pipe reader completes
                await _natsPipeliningWriteProtocolProcessor.WriteLoop.ConfigureAwait(false);
            }
            finally
            {
                await _natsPipeliningWriteProtocolProcessor.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public ValueTask WriteCommandAsync(Action<ProtocolWriter> commandFunc, CancellationToken cancellationToken = default) => _commandWriter.WriteCommandAsync(commandFunc, cancellationToken);
}
