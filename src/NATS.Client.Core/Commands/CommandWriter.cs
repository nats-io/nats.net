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
    private readonly PipeReader _pipeReader;
    private readonly PipeWriter _pipeWriter;
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
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: _opts.WriterBufferSize, // flush will block after hitting
            resumeWriterThreshold: _opts.WriterBufferSize / 2,
            useSynchronizationContext: false));
        _pipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;
        _readerLoopTask = Task.Run(ReaderLoopAsync);
    }

    public void Reset(ISocketConnection? socketConnection)
    {
        lock (_lock)
        {
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

        await _pipeWriter.CompleteAsync().ConfigureAwait(false);
        await _pipeReader.CompleteAsync().ConfigureAwait(false);

        await _readerLoopTask.ConfigureAwait(false);
    }

    public async ValueTask ConnectAsync(ClientOpts connectOpts, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        try
        {
            await _channelLock.Writer.WriteAsync(1, cancellationToken).ConfigureAwait(false);
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

            _protocolWriter.WriteConnect(_pipeWriter, connectOpts);
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        if (!await LockAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _enqueuePing(pingCommand);

            _protocolWriter.WritePing(_pipeWriter);
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public async ValueTask PongAsync(CancellationToken cancellationToken = default)
    {
        if (!await LockAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WritePong(_pipeWriter);
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(true);
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
        if (!await LockAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WriteSubscribe(_pipeWriter, sid, subject, queueGroup, maxMsgs);
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public async ValueTask UnsubscribeAsync(int sid, int? maxMsgs, CancellationToken cancellationToken)
    {
        if (!await LockAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CommandWriter));
            }

            _protocolWriter.WriteUnsubscribe(_pipeWriter, sid, maxMsgs);
            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PublishLockedAsync(string subject, string? replyTo,  NatsPooledBufferWriter<byte> payloadBuffer, NatsPooledBufferWriter<byte>? headersBuffer, CancellationToken cancellationToken)
    {
        if (!await LockAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

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

            await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await UnLockAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task UnLockAsync(CancellationToken cancellationToken)
    {
        while (!_channelLock.Reader.TryRead(out _))
        {
            try
            {
                await _channelLock.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Interlocked.Decrement(ref _counter.PendingMessages);
    }

    private async Task<bool> LockAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _counter.PendingMessages);

        try
        {
            await _channelLock.Writer.WriteAsync(1, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ChannelClosedException)
        {
            return false;
        }

        return true;
    }

    private async Task ReaderLoopAsync()
    {
        var cancellationToken = _cts.Token;
        while (cancellationToken.IsCancellationRequested == false)
        {
            try
            {
                ISocketConnection? connection;
                lock (_lock)
                {
                    connection = _socketConnection;
                }

                if (connection == null)
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

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
                            await connection.SendAsync(memory).ConfigureAwait(false);
                            completed = buffer.End;
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
                    _pipeReader.AdvanceTo(completed);
                }

                if (result.IsCompleted)
                {
                    break;
                }
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
