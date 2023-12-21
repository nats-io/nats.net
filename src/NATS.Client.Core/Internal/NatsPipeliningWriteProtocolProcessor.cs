using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

internal sealed class NatsPipeliningWriteProtocolProcessor : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConnectionStatsCounter _counter;
    private readonly NatsOpts _opts;
    private readonly PipeReader _pipeReader;
    private readonly ChannelReader<QueuedCommand> _queuedCommandReader;
    private readonly ISocketConnection _socketConnection;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private int _disposed;

    public NatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection, CommandWriter commandWriter, NatsOpts opts, ConnectionStatsCounter counter)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _counter = counter;
        _opts = opts;
        _pipeReader = commandWriter.PipeReader;
        _queuedCommandReader = commandWriter.QueuedCommandsReader;
        _socketConnection = socketConnection;
        WriteLoop = Task.Run(WriteLoopAsync);
    }

    public Task WriteLoop { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
#if NET6_0
            _cancellationTokenSource.Cancel();
#else
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
#endif
            try
            {
                await WriteLoop.ConfigureAwait(false); // wait to drain writer
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task WriteLoopAsync()
    {
        var logger = _opts.LoggerFactory.CreateLogger<NatsPipeliningWriteProtocolProcessor>();
        var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);
        var cancellationToken = _cancellationTokenSource.Token;
        var pending = 0;
        var examinedOffset = 0;

        // memory segment used to consolidate multiple small memory chunks
        // 8192 is half of minimumSegmentSize in CommandWriter
        // it is also the default socket send buffer size
        var consolidateMem = new Memory<byte>(new byte[8192]);

        try
        {
            while (true)
            {
                var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var consumedPos = result.Buffer.Start;
                var examinedPos = result.Buffer.Start;
                var buffer = result.Buffer.Slice(examinedOffset);
                while (buffer.Length > 0)
                {
                    var sendMem = buffer.First;
                    if (buffer.Length > sendMem.Length && sendMem.Length < consolidateMem.Length)
                    {
                        // consolidate multiple small memory chunks into one
                        var consolidateSize = (int)Math.Min(consolidateMem.Length, buffer.Length);
                        buffer.Slice(0, consolidateSize).CopyTo(consolidateMem.Span);
                        sendMem = consolidateMem.Slice(0, consolidateSize);
                    }

                    // perform send
                    _stopwatch.Restart();
                    var sent = await _socketConnection.SendAsync(sendMem).ConfigureAwait(false);
                    _stopwatch.Stop();
                    Interlocked.Add(ref _counter.SentBytes, sent);
                    if (isEnabledTraceLogging)
                    {
                        logger.LogTrace("Socket.SendAsync. Size: {0}  Elapsed: {1}ms", sent, _stopwatch.Elapsed.TotalMilliseconds);
                    }

                    var consumed = 0;
                    while (consumed < sent)
                    {
                        if (pending == 0)
                        {
                            // peek next message size off the channel
                            // this should always return synchronously since queued commands are
                            // written before the buffer is flushed
                            if (_queuedCommandReader.TryPeek(out var queuedCommand))
                            {
                                pending = queuedCommand.Size;
                            }
                            else
                            {
                                throw new NatsException("pipe writer flushed without sending queued command");
                            }
                        }

                        if (pending <= sent - consumed)
                        {
                            // pop the message previously peeked off the channel
                            // this should always return synchronously since it is the only
                            // channel read operation and a peek has already been preformed
                            if (_queuedCommandReader.TryRead(out _))
                            {
                                // increment counter
                                Interlocked.Add(ref _counter.PendingMessages, -1);
                                Interlocked.Add(ref _counter.SentMessages, 1);
                            }
                            else
                            {
                                throw new NatsException("channel read by someone else after peek");
                            }

                            // mark the bytes as consumed, and reset pending
                            consumed += pending;
                            pending = 0;
                        }
                        else
                        {
                            // an entire command was not sent; decrement pending by
                            // the number of bytes from the command that was sent
                            pending += consumed - sent;
                            break;
                        }
                    }

                    if (consumed > 0)
                    {
                        // mark fully sent commands as consumed
                        consumedPos = buffer.GetPosition(consumed);
                        examinedOffset = sent - consumed;
                    }
                    else
                    {
                        // no commands were consumed
                        examinedOffset += sent;
                    }

                    // lop off sent bytes for next iteration
                    examinedPos = buffer.GetPosition(sent);
                    buffer = buffer.Slice(sent);
                }

                _pipeReader.AdvanceTo(consumedPos, examinedPos);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore, intentionally disposed
        }
        catch (SocketClosedException)
        {
            // ignore, will be handled in read loop
        }

        logger.LogDebug("WriteLoop finished.");
    }
}
