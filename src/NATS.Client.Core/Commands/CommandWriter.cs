using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

/// <summary>
/// Used to track commands that have been enqueued to the PipeReader
/// </summary>
internal readonly record struct QueuedCommand(int Size, int Trim = 0);

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
    private readonly PipeWriter _pipeWriter;
    private readonly ProtocolWriter _protocolWriter;
    private readonly ChannelWriter<QueuedCommand> _queuedCommandsWriter;
    private readonly SemaphoreSlim _semLock;
    private Task? _flushTask;
    private bool _disposed;

    public CommandWriter(NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _counter = counter;
        _defaultCommandTimeout = overrideCommandTimeout ?? opts.CommandTimeout;
        _enqueuePing = enqueuePing;
        _opts = opts;
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: opts.WriterBufferSize, // flush will block after hitting
            resumeWriterThreshold: opts.WriterBufferSize / 2,  // will start flushing again after catching up
            minimumSegmentSize: 16384, // segment that is part of an uninterrupted payload can be sent using socket.send
            useSynchronizationContext: false));
        PipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;
        _protocolWriter = new ProtocolWriter(_pipeWriter, opts.SubjectEncoding, opts.HeaderEncoding);
        var channel = Channel.CreateUnbounded<QueuedCommand>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        _semLock = new SemaphoreSlim(1);
        QueuedCommandsReader = channel.Reader;
        _queuedCommandsWriter = channel.Writer;
    }

    public PipeReader PipeReader { get; }

    public ChannelReader<QueuedCommand> QueuedCommandsReader { get; }

    public Queue<QueuedCommand> InFlightCommands { get; } = new();

    public async ValueTask DisposeAsync()
    {
        await _semLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queuedCommandsWriter.Complete();
            await _pipeWriter.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            _semLock.Release();
        }
    }

    public NatsPipeliningWriteProtocolProcessor CreateNatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection) => new(socketConnection, this, _opts, _counter);

    public ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return ConnectStateMachineAsync(false, connectOpts, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return ConnectStateMachineAsync(true, connectOpts, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var success = false;
            try
            {
                _protocolWriter.WriteConnect(connectOpts);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask PingAsync(PingCommand pingCommand, CancellationToken cancellationToken)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return PingStateMachineAsync(false, pingCommand, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return PingStateMachineAsync(true, pingCommand, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var success = false;
            try
            {
                _protocolWriter.WritePing();
                _enqueuePing(pingCommand);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return PongStateMachineAsync(false, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return PongStateMachineAsync(true, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var success = false;
            try
            {
                _protocolWriter.WritePong();
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return PublishStateMachineAsync(false, subject, value, headers, replyTo, serializer, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return PublishStateMachineAsync(true, subject, value, headers, replyTo, serializer, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var trim = 0;
            var success = false;
            try
            {
                trim = _protocolWriter.WritePublish(subject, value, headers, replyTo, serializer);
                success = true;
            }
            finally
            {
                EnqueueCommand(success, trim: trim);
            }
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SubscribeAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return SubscribeStateMachineAsync(false, sid, subject, queueGroup, maxMsgs, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return SubscribeStateMachineAsync(true, sid, subject, queueGroup, maxMsgs, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var success = false;
            try
            {
                _protocolWriter.WriteSubscribe(sid, subject, queueGroup, maxMsgs);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(int sid, CancellationToken cancellationToken)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return UnsubscribeStateMachineAsync(false, sid, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return UnsubscribeStateMachineAsync(true, sid, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            var success = false;
            try
            {
                _protocolWriter.WriteUnsubscribe(sid, null);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask ConnectStateMachineAsync(bool lockHeld, ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException();
            }
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            var success = false;
            try
            {
                _protocolWriter.WriteConnect(connectOpts);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }
    }

    private async ValueTask PingStateMachineAsync(bool lockHeld, PingCommand pingCommand, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException();
            }
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            var success = false;
            try
            {
                _protocolWriter.WritePing();
                _enqueuePing(pingCommand);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }
    }

    private async ValueTask PongStateMachineAsync(bool lockHeld, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException();
            }
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            var success = false;
            try
            {
                _protocolWriter.WritePong();
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }
    }

    private async ValueTask PublishStateMachineAsync<T>(bool lockHeld, string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException();
            }
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            var trim = 0;
            var success = false;
            try
            {
                trim = _protocolWriter.WritePublish(subject, value, headers, replyTo, serializer);
                success = true;
            }
            finally
            {
                EnqueueCommand(success, trim: trim);
            }
        }
        finally
        {
            _semLock.Release();
        }
    }

    private async ValueTask SubscribeStateMachineAsync(bool lockHeld, int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException();
            }
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            var success = false;
            try
            {
                _protocolWriter.WriteSubscribe(sid, subject, queueGroup, maxMsgs);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }
    }

    private async ValueTask UnsubscribeStateMachineAsync(bool lockHeld, int sid, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException();
            }
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            var success = false;
            try
            {
                _protocolWriter.WriteUnsubscribe(sid, null);
                success = true;
            }
            finally
            {
                EnqueueCommand(success);
            }
        }
        finally
        {
            _semLock.Release();
        }
    }

    /// <summary>
    /// Enqueues a command, and kicks off a flush
    /// </summary>
    /// <param name="success">
    /// Whether the command was successful
    /// If true, it will be sent on the wire
    /// If false, it will be thrown out
    /// </param>
    /// <param name="trim">
    /// Number of bytes to skip from beginning of message
    /// when sending on the wire
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnqueueCommand(bool success, int trim = 0)
    {
        if (_pipeWriter.UnflushedBytes == 0)
        {
            // no unflushed bytes means no command was produced
            _flushTask = null;
            return;
        }

        if (success)
        {
            Interlocked.Add(ref _counter.PendingMessages, 1);
        }

        var size = (int)_pipeWriter.UnflushedBytes;
        _queuedCommandsWriter.TryWrite(new QueuedCommand(Size: size, Trim: success ? trim : size));
        var flush = _pipeWriter.FlushAsync();
        _flushTask = flush.IsCompletedSuccessfully ? null : flush.AsTask();
    }
}

internal sealed class PriorityCommandWriter : IAsyncDisposable
{
    private readonly NatsPipeliningWriteProtocolProcessor _natsPipeliningWriteProtocolProcessor;
    private int _disposed;

    public PriorityCommandWriter(ISocketConnection socketConnection, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing)
    {
        CommandWriter = new CommandWriter(opts, counter, enqueuePing, overrideCommandTimeout: TimeSpan.MaxValue);
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
