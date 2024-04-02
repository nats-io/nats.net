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
    // memory segment used to consolidate multiple small memory chunks
    // 8520 should fit into 6 packets on 1500 MTU TLS connection or 1 packet on 9000 MTU TLS connection
    // assuming 40 bytes TCP overhead + 40 bytes TLS overhead per packet
    private const int SendMemSize = 8520;

    // should be more than SendMemSize
    // https://github.com/nats-io/nats.net.v2/pull/383#discussion_r1484344102
    private const int MinSegmentSize = 16384;

    private readonly ILogger<CommandWriter> _logger;
    private readonly NatsConnection _connection;
    private readonly ObjectPool _pool;
    private readonly int _arrayPoolInitialSize;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts;
    private readonly ConnectionStatsCounter _counter;
    private readonly Memory<byte> _consolidateMem = new byte[SendMemSize].AsMemory();
    private readonly TimeSpan _defaultCommandTimeout;
    private readonly Action<PingCommand> _enqueuePing;
    private readonly ProtocolWriter _protocolWriter;
    private readonly HeaderWriter _headerWriter;
    private readonly Channel<int> _channelLock;
    private readonly Channel<int> _channelSize;
    private readonly PipeReader _pipeReader;
    private readonly PipeWriter _pipeWriter;
    private readonly SemaphoreSlim _semLock = new(1);
    private readonly PartialSendFailureCounter _partialSendFailureCounter = new();
    private ISocketConnection? _socketConnection;
    private Task? _flushTask;
    private Task? _readerLoopTask;
    private CancellationTokenSource? _ctsReader;
    private volatile bool _disposed;

    public CommandWriter(NatsConnection connection, ObjectPool pool, NatsOpts opts, ConnectionStatsCounter counter, Action<PingCommand> enqueuePing, TimeSpan? overrideCommandTimeout = default)
    {
        _logger = opts.LoggerFactory.CreateLogger<CommandWriter>();
        _connection = connection;
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
            minimumSegmentSize: MinSegmentSize,
            useSynchronizationContext: false));
        _pipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;
    }

    public void Reset(ISocketConnection socketConnection)
    {
        lock (_lock)
        {
            _socketConnection = socketConnection;
            _ctsReader = new CancellationTokenSource();

            _readerLoopTask = Task.Run(async () =>
            {
                await ReaderLoopAsync(
                    _logger,
                    _socketConnection,
                    _pipeReader,
                    _channelSize,
                    _consolidateMem,
                    _partialSendFailureCounter,
                    _ctsReader.Token)
                .ConfigureAwait(false);
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

            _protocolWriter.WriteConnect(_pipeWriter, connectOpts);
            EnqueueCommand();
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

            _protocolWriter.WritePing(_pipeWriter);
            _enqueuePing(pingCommand);
            EnqueueCommand();
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

            _protocolWriter.WritePong(_pipeWriter);
            EnqueueCommand();
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync<T>(string subject, T? value, NatsHeaders? headers, string? replyTo, INatsSerialize<T> serializer, CancellationToken cancellationToken)
    {
        NatsPooledBufferWriter<byte>? headersBuffer = null;
        if (headers != null)
        {
            if (!_pool.TryRent(out headersBuffer))
                headersBuffer = new NatsPooledBufferWriter<byte>(_arrayPoolInitialSize);
        }

        NatsPooledBufferWriter<byte> payloadBuffer;
        if (!_pool.TryRent(out payloadBuffer!))
            payloadBuffer = new NatsPooledBufferWriter<byte>(_arrayPoolInitialSize);

        try
        {
            if (headers != null)
                _headerWriter.Write(headersBuffer!, headers);

            if (value != null)
                serializer.Serialize(payloadBuffer, value);

            var size = payloadBuffer.WrittenMemory.Length + (headersBuffer?.WrittenMemory.Length ?? 0);
            if (_connection.ServerInfo is { } info && size > info.MaxPayload)
            {
                throw new NatsException($"Payload size {size} exceeds server's maximum payload size {info.MaxPayload}");
            }
        }
        catch
        {
            payloadBuffer.Reset();
            _pool.Return(payloadBuffer);

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }

            throw;
        }

#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return PublishStateMachineAsync(false, subject, replyTo, headersBuffer, payloadBuffer, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return PublishStateMachineAsync(true, subject, replyTo, headersBuffer, payloadBuffer, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WritePublish(_pipeWriter, subject, replyTo, headersBuffer?.WrittenMemory, payloadBuffer.WrittenMemory);
            EnqueueCommand();
        }
        finally
        {
            _semLock.Release();

            payloadBuffer.Reset();
            _pool.Return(payloadBuffer);

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }
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

            _protocolWriter.WriteSubscribe(_pipeWriter, sid, subject, queueGroup, maxMsgs);
            EnqueueCommand();
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
#pragma warning disable CA2016
#pragma warning disable VSTHRD103
        if (!_semLock.Wait(0))
#pragma warning restore VSTHRD103
#pragma warning restore CA2016
        {
            return UnsubscribeStateMachineAsync(false, sid, maxMsgs, cancellationToken);
        }

        if (_flushTask is { IsCompletedSuccessfully: false })
        {
            return UnsubscribeStateMachineAsync(true, sid, maxMsgs, cancellationToken);
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WriteUnsubscribe(_pipeWriter, sid, maxMsgs);
            EnqueueCommand();
        }
        finally
        {
            _semLock.Release();
        }

        return ValueTask.CompletedTask;
    }

    // only used for internal testing
    internal async Task TestStallFlushAsync(TimeSpan timeSpan)
    {
        await _semLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_flushTask is { IsCompletedSuccessfully: false })
            {
                await _flushTask.ConfigureAwait(false);
            }

            _flushTask = Task.Delay(timeSpan);
        }
        finally
        {
            _semLock.Release();
        }
    }

    private static async Task ReaderLoopAsync(
        ILogger<CommandWriter> logger,
        ISocketConnection connection,
        PipeReader pipeReader,
        Channel<int> channelSize,
        Memory<byte> consolidateMem,
        PartialSendFailureCounter partialSendFailureCounter,
        CancellationToken cancellationToken)
    {
        try
        {
            var examinedOffset = 0;
            var pending = 0;
            while (true)
            {
                var result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (result.IsCanceled)
                {
                    break;
                }

                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.GetPosition(examinedOffset);
                buffer = result.Buffer.Slice(examinedOffset);

                try
                {
                    while (!buffer.IsEmpty)
                    {
                        var sendMem = buffer.First;
                        if (sendMem.Length > SendMemSize)
                        {
                            sendMem = sendMem[..SendMemSize];
                        }
                        else if (sendMem.Length < SendMemSize && buffer.Length > sendMem.Length)
                        {
                            var consolidateLen = Math.Min(SendMemSize, (int)buffer.Length);
                            buffer.Slice(0, consolidateLen).CopyTo(consolidateMem.Span);
                            sendMem = consolidateMem[..consolidateLen];
                        }

                        int sent;
                        Exception? sendEx = null;
                        try
                        {
                            sent = await connection.SendAsync(sendMem).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // we have no idea how many bytes were actually sent, so we have to assume they all were
                            // this could result in message loss, but is consistent with at-most once delivery
                            sendEx = ex;
                            sent = sendMem.Length;
                        }

                        var totalSize = 0;
                        while (totalSize < sent)
                        {
                            if (pending == 0)
                            {
                                while (!channelSize.Reader.TryPeek(out pending))
                                {
                                    // should never happen; channel sizes are written before flush is called
                                    await channelSize.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                                }
                            }

                            // don't mark the message as complete if we have more data to send
                            if (totalSize + pending > sent)
                            {
                                pending += totalSize - sent;
                                break;
                            }

                            while (!channelSize.Reader.TryRead(out _))
                            {
                                // should never happen; channel sizes are written before flush is called
                                await channelSize.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                            }

                            totalSize += pending;
                            examinedOffset = 0;
                            pending = 0;
                        }

                        // only mark bytes as consumed if a full command was sent
                        if (totalSize > 0)
                        {
                            // mark totalSize bytes as consumed
                            consumed = buffer.GetPosition(totalSize);

                            // reset the partialSendFailureCounter, since a full command was consumed
                            partialSendFailureCounter.Reset();
                        }

                        // mark sent bytes as examined
                        examined = buffer.GetPosition(sent);
                        examinedOffset += sent - totalSize;

                        // slice the buffer for next iteration
                        buffer = buffer.Slice(sent);

                        // throw if there was a send failure
                        if (sendEx != null)
                        {
                            if (pending > 0)
                            {
                                // there was a partially sent command
                                // if this command is re-sent and fails again, it most likely means
                                // that the command is malformed and the nats-server is closing
                                // the connection with an error.  we want to throw this command
                                // away if partialSendFailureCounter.Failed() returns true
                                if (partialSendFailureCounter.Failed())
                                {
                                    // throw away the rest of the partially sent command if it's in the buffer
                                    if (buffer.Length >= pending)
                                    {
                                        consumed = buffer.GetPosition(pending);
                                        examined = buffer.GetPosition(pending);
                                        partialSendFailureCounter.Reset();
                                        while (!channelSize.Reader.TryRead(out _))
                                        {
                                            // should never happen; channel sizes are written before flush is called
                                            await channelSize.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                                        }
                                    }
                                }
                                else
                                {
                                    // increment the counter
                                    partialSendFailureCounter.Increment();
                                }
                            }

                            throw sendEx;
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
        catch (ObjectDisposedException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            logger.LogError(NatsLogEvents.Buffer, e, "Unexpected error in send buffer reader loop");
        }
    }

    /// <summary>
    /// Enqueues a command, and kicks off a flush
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnqueueCommand()
    {
        var size = (int)_pipeWriter.UnflushedBytes;
        if (size == 0)
        {
            // no unflushed bytes means no command was produced
            _flushTask = null;
            return;
        }

        Interlocked.Add(ref _counter.PendingMessages, 1);

        _channelSize.Writer.TryWrite(size);
        var flush = _pipeWriter.FlushAsync();
        _flushTask = flush.IsCompletedSuccessfully ? null : flush.AsTask();
    }

    private async ValueTask ConnectStateMachineAsync(bool lockHeld, ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new OperationCanceledException();
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

            _protocolWriter.WriteConnect(_pipeWriter, connectOpts);
            EnqueueCommand();
        }
        catch (TimeoutException)
        {
            // WaitAsync throws a TimeoutException when the TimeSpan is exceeded
            // standardize to an OperationCanceledException as if a cancellationToken was used
            throw new OperationCanceledException();
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
                throw new OperationCanceledException();
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

            _protocolWriter.WritePing(_pipeWriter);
            _enqueuePing(pingCommand);
            EnqueueCommand();
        }
        catch (TimeoutException)
        {
            // WaitAsync throws a TimeoutException when the TimeSpan is exceeded
            // standardize to an OperationCanceledException as if a cancellationToken was used
            throw new OperationCanceledException();
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
                throw new OperationCanceledException();
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

            _protocolWriter.WritePong(_pipeWriter);
            EnqueueCommand();
        }
        catch (TimeoutException)
        {
            // WaitAsync throws a TimeoutException when the TimeSpan is exceeded
            // standardize to an OperationCanceledException as if a cancellationToken was used
            throw new OperationCanceledException();
        }
        finally
        {
            _semLock.Release();
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PublishStateMachineAsync(bool lockHeld, string subject, string? replyTo, NatsPooledBufferWriter<byte>? headersBuffer, NatsPooledBufferWriter<byte> payloadBuffer, CancellationToken cancellationToken)
    {
        try
        {
            if (!lockHeld)
            {
                if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
                {
                    throw new OperationCanceledException();
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

                _protocolWriter.WritePublish(_pipeWriter, subject, replyTo, headersBuffer?.WrittenMemory, payloadBuffer.WrittenMemory);
                EnqueueCommand();
            }
            catch (TimeoutException)
            {
                // WaitAsync throws a TimeoutException when the TimeSpan is exceeded
                // standardize to an OperationCanceledException as if a cancellationToken was used
                throw new OperationCanceledException();
            }
            finally
            {
                _semLock.Release();
            }
        }
        finally
        {
            payloadBuffer.Reset();
            _pool.Return(payloadBuffer);

            if (headersBuffer != null)
            {
                headersBuffer.Reset();
                _pool.Return(headersBuffer);
            }
        }
    }

    private async ValueTask SubscribeStateMachineAsync(bool lockHeld, int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new OperationCanceledException();
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

            _protocolWriter.WriteSubscribe(_pipeWriter, sid, subject, queueGroup, maxMsgs);
            EnqueueCommand();
        }
        catch (TimeoutException)
        {
            // WaitAsync throws a TimeoutException when the TimeSpan is exceeded
            // standardize to an OperationCanceledException as if a cancellationToken was used
            throw new OperationCanceledException();
        }
        finally
        {
            _semLock.Release();
        }
    }

    private async ValueTask UnsubscribeStateMachineAsync(bool lockHeld, int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        if (!lockHeld)
        {
            if (!await _semLock.WaitAsync(_defaultCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new OperationCanceledException();
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

            _protocolWriter.WriteUnsubscribe(_pipeWriter, sid, maxMsgs);
            EnqueueCommand();
        }
        catch (TimeoutException)
        {
            // WaitAsync throws a TimeoutException when the TimeSpan is exceeded
            // standardize to an OperationCanceledException as if a cancellationToken was used
            throw new OperationCanceledException();
        }
        finally
        {
            _semLock.Release();
        }
    }

    private class PartialSendFailureCounter
    {
        private const int MaxRetry = 1;
        private readonly object _gate = new();
        private int _count;

        public bool Failed()
        {
            lock (_gate)
            {
                return _count >= MaxRetry;
            }
        }

        public void Increment()
        {
            lock (_gate)
            {
                _count++;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _count = 0;
            }
        }
    }
}
