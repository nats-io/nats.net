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
    private readonly Queue<QueuedCommand> _inFlightCommands;
    private readonly ChannelReader<QueuedCommand> _queuedCommandReader;
    private readonly ISocketConnection _socketConnection;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private int _disposed;

    public NatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection, CommandWriter commandWriter, NatsOpts opts, ConnectionStatsCounter counter)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _counter = counter;
        _inFlightCommands = commandWriter.InFlightCommands;
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
        var canceled = false;
        var examinedOffset = 0;

        // memory segment used to consolidate multiple small memory chunks
        // should <= (minimumSegmentSize * 0.5) in CommandWriter
        var consolidateMem = new Memory<byte>(new byte[8000]);

        // add up in flight command sum
        var inFlightSum = 0;
        foreach (var command in _inFlightCommands)
        {
            inFlightSum += command.Size;
        }

        try
        {
            while (true)
            {
                var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                while (inFlightSum < result.Buffer.Length)
                {
                    QueuedCommand queuedCommand;
                    while (!_queuedCommandReader.TryRead(out queuedCommand))
                    {
                        await _queuedCommandReader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                    }

                    _inFlightCommands.Enqueue(queuedCommand);
                    inFlightSum += queuedCommand.Size;
                }

                var consumedPos = result.Buffer.Start;
                var examinedPos = result.Buffer.Start;
                var buffer = result.Buffer.Slice(examinedOffset);

                while (buffer.Length > 0)
                {
                    if (pending == 0)
                    {
                        var peek = _inFlightCommands.Peek();
                        pending = peek.Size;
                        canceled = peek.Canceled;
                    }

                    if (canceled)
                    {
                        if (pending > buffer.Length)
                        {
                            // command partially canceled
                            examinedPos = buffer.GetPosition(examinedOffset);
                            examinedOffset = (int)buffer.Length;
                            pending -= (int)buffer.Length;
                            break;
                        }

                        // command completely canceled
                        inFlightSum -= _inFlightCommands.Dequeue().Size;
                        consumedPos = buffer.GetPosition(pending);
                        examinedPos = buffer.GetPosition(pending);
                        examinedOffset = 0;
                        buffer = buffer.Slice(pending);
                        pending = 0;
                        continue;
                    }

                    var sendMem = buffer.First;
                    var maxSize = 0;
                    var maxSizeCap = Math.Max(sendMem.Length, consolidateMem.Length);
                    foreach (var command in _inFlightCommands)
                    {
                        if (maxSize == 0)
                        {
                            // first command; set to pending
                            maxSize = pending;
                            continue;
                        }

                        if (maxSize > maxSizeCap || command.Canceled)
                        {
                            break;
                        }

                        // next command can be sent also
                        maxSize += command.Size;
                    }

                    if (sendMem.Length > maxSize)
                    {
                        sendMem = sendMem[..maxSize];
                    }
                    else
                    {
                        var bufferIter = buffer.Slice(0, Math.Min(maxSize, buffer.Length));
                        if (bufferIter.Length > sendMem.Length && sendMem.Length < consolidateMem.Length)
                        {
                            // consolidate multiple small memory chunks into one
                            var consolidateSize = (int)Math.Min(consolidateMem.Length, bufferIter.Length);
                            bufferIter.Slice(0, consolidateSize).CopyTo(consolidateMem.Span);
                            sendMem = consolidateMem[..consolidateSize];
                        }
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
                            pending = _inFlightCommands.Peek().Size;
                        }

                        if (pending <= sent - consumed)
                        {
                            inFlightSum -= _inFlightCommands.Dequeue().Size;
                            Interlocked.Add(ref _counter.PendingMessages, -1);
                            Interlocked.Add(ref _counter.SentMessages, 1);

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
                if (result.IsCompleted)
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
