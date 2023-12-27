using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

// QueuedCommand is used to track commands that have been queued but not sent
internal readonly record struct QueuedCommand(int Size)
{
}

internal sealed class CommandWriter : IAsyncDisposable
{
    private readonly ConnectionStatsCounter _counter;
    private readonly NatsOpts _opts;
    private readonly PipeWriter _pipeWriter;
    private readonly ProtocolWriter _protocolWriter;
    private readonly ChannelWriter<QueuedCommand> _queuedCommandsWriter;
    private readonly SemaphoreSlim _sem;
    private bool _disposed;

    public CommandWriter(NatsOpts opts, ConnectionStatsCounter counter)
    {
        _counter = counter;
        _opts = opts;
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: opts.WriterBufferSize, resumeWriterThreshold: opts.WriterBufferSize / 2, minimumSegmentSize: 65536, useSynchronizationContext: false));
        PipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;
        _protocolWriter = new ProtocolWriter(_pipeWriter, opts.SubjectEncoding, opts.HeaderEncoding);
        var channel = Channel.CreateUnbounded<QueuedCommand>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        _sem = new SemaphoreSlim(1);
        QueuedCommandsReader = channel.Reader;
        _queuedCommandsWriter = channel.Writer;
    }

    public PipeReader PipeReader { get; }

    public ChannelReader<QueuedCommand> QueuedCommandsReader { get; }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (!_sem.Wait(0))
            {
                await _sem.WaitAsync().ConfigureAwait(false);
            }

            try
            {
                _disposed = true;
                _queuedCommandsWriter.Complete();
                await _pipeWriter.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                _sem.Release();
            }
        }
    }

    public NatsPipeliningWriteProtocolProcessor CreateNatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection) => new(socketConnection, this, _opts, _counter);

    public async ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _protocolWriter.WriteConnect(connectOpts);
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async ValueTask DirectWriteAsync(string protocol, int repeatCount, CancellationToken cancellationToken)
    {
        if (repeatCount < 1)
            throw new ArgumentException("repeatCount should >= 1, repeatCount:" + repeatCount);

        byte[] protocolBytes;
        if (repeatCount == 1)
        {
            protocolBytes = Encoding.UTF8.GetBytes(protocol + "\r\n");
        }
        else
        {
            var bin = Encoding.UTF8.GetBytes(protocol + "\r\n");
            protocolBytes = new byte[bin.Length * repeatCount];
            var span = protocolBytes.AsMemory();
            for (var i = 0; i < repeatCount; i++)
            {
                bin.CopyTo(span);
                span = span.Slice(bin.Length);
            }
        }

        if (!_sem.Wait(0))
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _protocolWriter.WriteRaw(protocolBytes);
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async ValueTask PingAsync(CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _protocolWriter.WritePing();
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async ValueTask PongAsync(CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _protocolWriter.WritePong();
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }

    public ValueTask PublishAsync<T>(string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            return AwaitLockAndPublishAsync(subject, replyTo, headers, value, serializer, cancellationToken);
        }

        try
        {
            _protocolWriter.WritePublish(subject, replyTo, headers, value, serializer);
        }
        catch
        {
            _sem.Release();
            throw;
        }

        Interlocked.Add(ref _counter.PendingMessages, 1);
        _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));

        ValueTask<FlushResult> flush;
        try
        {
            flush = _pipeWriter.FlushAsync(cancellationToken);
        }
        catch
        {
            _sem.Release();
            throw;
        }

        if (flush.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD103 // Call async methods when in an async method
            flush.GetAwaiter().GetResult();
#pragma warning restore VSTHRD103 // Call async methods when in an async method
            _sem.Release();
            return ValueTask.CompletedTask;
        }

        return AwaitFlushAsync(flush);
    }

    public ValueTask PublishBytesAsync(string subject, string? replyTo, NatsHeaders? headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            return AwaitLockAndPublishBytesAsync(subject, replyTo, headers, payload, cancellationToken);
        }

        try
        {
            _protocolWriter.WritePublish(subject, replyTo, headers, payload);
        }
        catch
        {
            _sem.Release();
            throw;
        }

        Interlocked.Add(ref _counter.PendingMessages, 1);
        _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));

        ValueTask<FlushResult> flush;
        try
        {
            flush = _pipeWriter.FlushAsync(cancellationToken);
        }
        catch
        {
            _sem.Release();
            throw;
        }

        if (flush.IsCompletedSuccessfully)
        {
#pragma warning disable VSTHRD103 // Call async methods when in an async method
            flush.GetAwaiter().GetResult();
#pragma warning restore VSTHRD103 // Call async methods when in an async method
            _sem.Release();
            return ValueTask.CompletedTask;
        }

        return AwaitFlushAsync(flush);
    }

    public async ValueTask SubscribeAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _protocolWriter.WriteSubscribe(sid, subject, queueGroup, maxMsgs);
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async ValueTask UnsubscribeAsync(int sid, CancellationToken cancellationToken)
    {
        if (!_sem.Wait(0))
        {
            await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _protocolWriter.WriteUnsubscribe(sid, null);
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }

    private async ValueTask AwaitLockAndPublishAsync<T>(string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _protocolWriter.WritePublish(subject, replyTo, headers, value, serializer);
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            var flush = _pipeWriter.FlushAsync(cancellationToken);
            if (flush.IsCompletedSuccessfully)
            {
#pragma warning disable VSTHRD103 // Call async methods when in an async method
                flush.GetAwaiter().GetResult();
#pragma warning restore VSTHRD103 // Call async methods when in an async method
            }
            else
            {
                await flush.ConfigureAwait(false);
            }
        }
        finally
        {
            _sem.Release();
        }
    }

    private async ValueTask AwaitLockAndPublishBytesAsync(string subject, string? replyTo, NatsHeaders? headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken)
    {
        await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _protocolWriter.WritePublish(subject, replyTo, headers, payload);
            Interlocked.Add(ref _counter.PendingMessages, 1);
            _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: (int)_pipeWriter.UnflushedBytes));
            var flush = _pipeWriter.FlushAsync(cancellationToken);
            if (flush.IsCompletedSuccessfully)
            {
#pragma warning disable VSTHRD103 // Call async methods when in an async method
                flush.GetAwaiter().GetResult();
#pragma warning restore VSTHRD103 // Call async methods when in an async method
            }
            else
            {
                await flush.ConfigureAwait(false);
            }
        }
        finally
        {
            _sem.Release();
        }
    }

    private async ValueTask AwaitFlushAsync(ValueTask<FlushResult> flush)
    {
        try
        {
            await flush.ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }
}

internal sealed class PriorityCommandWriter : IAsyncDisposable
{
    private readonly NatsPipeliningWriteProtocolProcessor _natsPipeliningWriteProtocolProcessor;
    private int _disposed;

    public PriorityCommandWriter(ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter)
    {
        CommandWriter = new CommandWriter(opts, counter);
        _natsPipeliningWriteProtocolProcessor = CommandWriter.CreateNatsPipeliningWriteProtocolProcessor(socketConnection);
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
                await _natsPipeliningWriteProtocolProcessor.WriteLoop.ConfigureAwait(false);
            }
            finally
            {
                await _natsPipeliningWriteProtocolProcessor.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
