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
        var examined = 0;

        try
        {
            while (true)
            {
                var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer.Slice(examined);
                if (buffer.Length > 0)
                {
                    // perform send
                    _stopwatch.Restart();
                    var sent = await _socketConnection.SendAsync(buffer.First).ConfigureAwait(false);
                    _stopwatch.Stop();
                    Interlocked.Add(ref _counter.SentBytes, sent);
                    if (isEnabledTraceLogging)
                    {
                        logger.LogTrace("Socket.SendAsync. Size: {0}  Elapsed: {1}ms", sent, _stopwatch.Elapsed.TotalMilliseconds);
                    }

                    var consumed = 0;
                    while (true)
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

                        if (pending <= sent)
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
                            pending -= sent + consumed;
                            break;
                        }
                    }

                    // advance the pipe reader by marking entirely sent commands as consumed,
                    // and the number of bytes sent as examined
                    _pipeReader.AdvanceTo(buffer.GetPosition(consumed), buffer.GetPosition(sent));

                    // update examined for slicing the buffer next iteration
                    examined += sent - consumed;
                }

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
