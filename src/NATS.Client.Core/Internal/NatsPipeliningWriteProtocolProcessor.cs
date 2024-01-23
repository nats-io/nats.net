using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

internal sealed class NatsPipeliningWriteProtocolProcessor : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
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
        _cts = new CancellationTokenSource();
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
            _cts.Cancel();
#else
            await _cts.CancelAsync().ConfigureAwait(false);
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
        var cancellationToken = _cts.Token;
        var pending = 0;
        var trimming = 0;
        var examinedOffset = 0;
        var sent = 0;

        // memory segment used to consolidate multiple small memory chunks
        // should <= (minimumSegmentSize * 0.5) in CommandWriter
        // 8520 should fit into 6 packets on 1500 MTU TLS connection or 1 packet on 9000 MTU TLS connection
        // assuming 40 bytes TCP overhead + 40 bytes TLS overhead per packet
        var consolidateMem = new Memory<byte>(new byte[8520]);

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
                // read data from pipe reader
                var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (result.IsCanceled)
                {
                    // if the pipe has been canceled, break
                    break;
                }

                var consumedPos = result.Buffer.Start;
                var examinedPos = result.Buffer.Start;
                try
                {
                    // move from _queuedCommandReader to _inFlightCommands until the total size
                    // of all _inFlightCommands is >= result.Buffer.Length
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

                    // examinedOffset was processed last iteration, so slice it off the buffer
                    var buffer = result.Buffer.Slice(examinedOffset);

                    // iterate until buffer is empty
                    // any time buffer sliced and re-assigned, continue should be called
                    // so that this conditional is checked
                    while (buffer.Length > 0)
                    {
                        // if there are no pending bytes to send, set to next command
                        if (pending == 0)
                        {
                            var peek = _inFlightCommands.Peek();
                            pending = peek.Size;
                            trimming = peek.Trim;
                        }

                        // from this point forward, pending != 0
                        // any operation that decrements pending should check if it is 0,
                        // and dequeue from _inFlightCommands if it is

                        // trim any bytes that should not be sent
                        if (trimming > 0)
                        {
                            var trimmed = Math.Min(trimming, (int)buffer.Length);
                            pending -= trimmed;
                            trimming -= trimmed;
                            if (pending == 0)
                            {
                                // the entire command was trimmed (canceled)
                                inFlightSum -= _inFlightCommands.Dequeue().Size;
                                consumedPos = buffer.GetPosition(trimmed);
                                examinedPos = consumedPos;
                                examinedOffset = 0;
                                buffer = buffer.Slice(trimmed);

                                // iterate in case buffer is now empty
                                continue;
                            }

                            // the command was partially trimmed
                            examinedPos = buffer.GetPosition(trimmed);
                            examinedOffset += trimmed;
                            buffer = buffer.Slice(trimmed);

                            // iterate in case buffer is now empty
                            continue;
                        }

                        if (sent > 0)
                        {
                            if (pending <= sent)
                            {
                                // the entire command was sent
                                inFlightSum -= _inFlightCommands.Dequeue().Size;
                                Interlocked.Add(ref _counter.PendingMessages, -1);
                                Interlocked.Add(ref _counter.SentMessages, 1);

                                // mark the bytes as consumed, and reset pending
                                sent -= pending;
                                consumedPos = buffer.GetPosition(pending);
                                examinedPos = consumedPos;
                                examinedOffset = 0;
                                buffer = buffer.Slice(pending);
                                pending = 0;

                                // iterate in case buffer is now empty
                                continue;
                            }

                            // the command was partially sent
                            // decrement pending by the number of bytes that were sent
                            pending -= sent;
                            examinedPos = buffer.GetPosition(sent);
                            examinedOffset += sent;
                            buffer = buffer.Slice(sent);
                            sent = 0;

                            // iterate in case buffer is now empty
                            continue;
                        }

                        // loop through _inFlightCommands to determine whether any commands
                        // in the first memory segment need trimming
                        var sendMem = buffer.First;
                        var maxSize = 0;
                        var maxSizeCap = Math.Max(sendMem.Length, consolidateMem.Length);
                        var doTrim = false;
                        foreach (var command in _inFlightCommands)
                        {
                            if (maxSize == 0)
                            {
                                // first command; set to pending
                                maxSize = pending;
                                continue;
                            }

                            if (maxSize > maxSizeCap)
                            {
                                // over cap
                                break;
                            }

                            if (command.Trim > 0)
                            {
                                // will have to trim
                                doTrim = true;
                                break;
                            }

                            maxSize += command.Size;
                        }

                        // adjust the first memory segment to end on a command boundary
                        if (sendMem.Length > maxSize)
                        {
                            sendMem = sendMem[..maxSize];
                        }

                        // if trimming is required or the first memory segment is smaller than consolidateMem
                        // consolidate bytes that need to be sent into consolidateMem
                        if (doTrim || (buffer.Length > sendMem.Length && sendMem.Length < consolidateMem.Length))
                        {
                            var bufferIter = buffer;
                            var memIter = consolidateMem;
                            var trimmedSize = 0;
                            foreach (var command in _inFlightCommands)
                            {
                                if (bufferIter.Length == 0 || memIter.Length == 0)
                                {
                                    break;
                                }

                                int write;
                                if (trimmedSize == 0)
                                {
                                    // first command, only write pending data
                                    write = pending;
                                }
                                else if (command.Trim == 0)
                                {
                                    write = command.Size;
                                }
                                else
                                {
                                    if (bufferIter.Length < command.Trim + 1)
                                    {
                                        // not enough bytes to start writing the next command
                                        break;
                                    }

                                    bufferIter = bufferIter.Slice(command.Trim);
                                    write = command.Size - command.Trim;
                                    if (write == 0)
                                    {
                                        // the entire command was trimmed (canceled)
                                        continue;
                                    }
                                }

                                write = Math.Min(memIter.Length, write);
                                write = Math.Min((int)bufferIter.Length, write);
                                bufferIter.Slice(0, write).CopyTo(memIter.Span);
                                memIter = memIter[write..];
                                bufferIter = bufferIter.Slice(write);
                                trimmedSize += write;
                            }

                            sendMem = consolidateMem[..trimmedSize];
                        }

                        // perform send
                        _stopwatch.Restart();
                        sent = await _socketConnection.SendAsync(sendMem).ConfigureAwait(false);
                        _stopwatch.Stop();
                        Interlocked.Add(ref _counter.SentBytes, sent);
                        if (isEnabledTraceLogging)
                        {
                            logger.LogTrace("Socket.SendAsync. Size: {0}  Elapsed: {1}ms", sent, _stopwatch.Elapsed.TotalMilliseconds);
                        }
                    }
                }
                finally
                {
                    // _pipeReader.AdvanceTo must be called exactly once for every
                    // _pipeReader.ReadAsync, which is why it is in the finally block
                    _pipeReader.AdvanceTo(consumedPos, examinedPos);
                }

                if (result.IsCompleted)
                {
                    // if the pipe has been completed, break
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occured in write loop");
            throw;
        }

        logger.LogDebug("WriteLoop finished.");
    }
}
