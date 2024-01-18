// using System.Buffers;
// using System.Diagnostics;
// using System.IO.Pipelines;
// using System.Threading.Channels;
// using Microsoft.Extensions.Logging;
// using NATS.Client.Core.Commands;
//
// namespace NATS.Client.Core.Internal;
//
// internal sealed class NatsPipeliningWriteProtocolProcessor : IAsyncDisposable
// {
//     private readonly CancellationTokenSource _cts;
//     private readonly ConnectionStatsCounter _counter;
//     private readonly NatsOpts _opts;
//     private readonly PipeReader _pipeReader;
//     private readonly Queue<QueuedCommand> _inFlightCommands;
//     private readonly ChannelReader<QueuedCommand> _queuedCommandReader;
//     private readonly ISocketConnection _socketConnection;
//     private readonly Stopwatch _stopwatch = new Stopwatch();
//     private int _disposed;
//
//     public NatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection, CommandWriter commandWriter, NatsOpts opts, ConnectionStatsCounter counter)
//     {
//         _cts = new CancellationTokenSource();
//         _counter = counter;
//         _inFlightCommands = commandWriter.InFlightCommands;
//         _opts = opts;
//         _pipeReader = commandWriter.PipeReader;
//         _queuedCommandReader = commandWriter.QueuedCommandsReader;
//         _socketConnection = socketConnection;
//         WriteLoop = Task.Run(WriteLoopAsync);
//     }
//
//     public Task WriteLoop { get; }
//
//     public async ValueTask DisposeAsync()
//     {
//         if (Interlocked.Increment(ref _disposed) == 1)
//         {
// #if NET6_0
//             _cts.Cancel();
// #else
//             await _cts.CancelAsync().ConfigureAwait(false);
// #endif
//             try
//             {
//                 await WriteLoop.ConfigureAwait(false); // wait to drain writer
//             }
//             catch
//             {
//                 // ignore
//             }
//         }
//     }
//
//     private async Task WriteLoopAsync()
//     {
//         var logger = _opts.LoggerFactory.CreateLogger<NatsPipeliningWriteProtocolProcessor>();
//         var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);
//         var cancellationToken = _cts.Token;
//         var pending = 0;
//         var trimming = 0;
//         var examinedOffset = 0;
//
//         // memory segment used to consolidate multiple small memory chunks
//         // should <= (minimumSegmentSize * 0.5) in CommandWriter
//         // 8520 should fit into 6 packets on 1500 MTU TLS connection or 1 packet on 9000 MTU TLS connection
//         // assuming 40 bytes TCP overhead + 40 bytes TLS overhead per packet
//         var consolidateMem = new Memory<byte>(new byte[8520]);
//
//         // add up in flight command sum
//         var inFlightSum = 0;
//         foreach (var command in _inFlightCommands)
//         {
//             inFlightSum += command.Size;
//         }
//
//         try
//         {
//             while (true)
//             {
//                 var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
//                 if (result.IsCanceled)
//                 {
//                     break;
//                 }
//
//                 var consumedPos = result.Buffer.Start;
//                 var examinedPos = result.Buffer.Start;
//                 try
//                 {
//                     var buffer = result.Buffer.Slice(examinedOffset);
//                     while (inFlightSum < result.Buffer.Length)
//                     {
//                         QueuedCommand queuedCommand;
//                         while (!_queuedCommandReader.TryRead(out queuedCommand))
//                         {
//                             await _queuedCommandReader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
//                         }
//
//                         _inFlightCommands.Enqueue(queuedCommand);
//                         inFlightSum += queuedCommand.Size;
//                     }
//
//                     while (buffer.Length > 0)
//                     {
//                         if (pending == 0)
//                         {
//                             var peek = _inFlightCommands.Peek();
//                             pending = peek.Size;
//                             trimming = peek.Trim;
//                         }
//
//                         if (trimming > 0)
//                         {
//                             var trimmed = Math.Min(trimming, (int)buffer.Length);
//                             consumedPos = buffer.GetPosition(trimmed);
//                             examinedPos = buffer.GetPosition(trimmed);
//                             examinedOffset = 0;
//                             buffer = buffer.Slice(trimmed);
//                             pending -= trimmed;
//                             trimming -= trimmed;
//                             if (pending == 0)
//                             {
//                                 // the entire command was trimmed (canceled)
//                                 inFlightSum -= _inFlightCommands.Dequeue().Size;
//                             }
//
//                             continue;
//                         }
//
//                         var sendMem = buffer.First;
//                         var maxSize = 0;
//                         var maxSizeCap = Math.Max(sendMem.Length, consolidateMem.Length);
//                         var doTrim = false;
//                         foreach (var command in _inFlightCommands)
//                         {
//                             if (maxSize == 0)
//                             {
//                                 // first command; set to pending
//                                 maxSize = pending;
//                                 continue;
//                             }
//
//                             if (maxSize > maxSizeCap)
//                             {
//                                 // over cap
//                                 break;
//                             }
//
//                             if (command.Trim > 0)
//                             {
//                                 // will have to trim
//                                 doTrim = true;
//                                 break;
//                             }
//
//                             maxSize += command.Size;
//                         }
//
//                         if (sendMem.Length > maxSize)
//                         {
//                             sendMem = sendMem[..maxSize];
//                         }
//
//                         var bufferIter = buffer;
//                         if (doTrim || (bufferIter.Length > sendMem.Length && sendMem.Length < consolidateMem.Length))
//                         {
//                             var memIter = consolidateMem;
//                             var trimmedSize = 0;
//                             foreach (var command in _inFlightCommands)
//                             {
//                                 if (bufferIter.Length == 0 || memIter.Length == 0)
//                                 {
//                                     break;
//                                 }
//
//                                 int write;
//                                 if (trimmedSize == 0)
//                                 {
//                                     // first command, only write pending data
//                                     write = pending;
//                                 }
//                                 else if (command.Trim == 0)
//                                 {
//                                     write = command.Size;
//                                 }
//                                 else
//                                 {
//                                     if (bufferIter.Length < command.Trim + 1)
//                                     {
//                                         // not enough bytes to start writing the next command
//                                         break;
//                                     }
//
//                                     bufferIter = bufferIter.Slice(command.Trim);
//                                     write = command.Size - command.Trim;
//                                     if (write == 0)
//                                     {
//                                         // the entire command was trimmed (canceled)
//                                         continue;
//                                     }
//                                 }
//
//                                 write = Math.Min(memIter.Length, write);
//                                 write = Math.Min((int)bufferIter.Length, write);
//                                 bufferIter.Slice(0, write).CopyTo(memIter.Span);
//                                 memIter = memIter[write..];
//                                 bufferIter = bufferIter.Slice(write);
//                                 trimmedSize += write;
//                             }
//
//                             sendMem = consolidateMem[..trimmedSize];
//                         }
//
//                         // perform send
//                         _stopwatch.Restart();
//                         var sent = await _socketConnection.SendAsync(sendMem).ConfigureAwait(false);
//                         _stopwatch.Stop();
//                         Interlocked.Add(ref _counter.SentBytes, sent);
//                         if (isEnabledTraceLogging)
//                         {
//                             logger.LogTrace("Socket.SendAsync. Size: {0}  Elapsed: {1}ms", sent, _stopwatch.Elapsed.TotalMilliseconds);
//                         }
//
//                         var consumed = 0;
//                         var sentAndTrimmed = sent;
//                         while (consumed < sentAndTrimmed)
//                         {
//                             if (pending == 0)
//                             {
//                                 var peek = _inFlightCommands.Peek();
//                                 pending = peek.Size - peek.Trim;
//                                 consumed += peek.Trim;
//                                 sentAndTrimmed += peek.Trim;
//
//                                 if (pending == 0)
//                                 {
//                                     // the entire command was trimmed (canceled)
//                                     inFlightSum -= _inFlightCommands.Dequeue().Size;
//                                     continue;
//                                 }
//                             }
//
//                             if (pending <= sentAndTrimmed - consumed)
//                             {
//                                 // the entire command was sent
//                                 inFlightSum -= _inFlightCommands.Dequeue().Size;
//                                 Interlocked.Add(ref _counter.PendingMessages, -1);
//                                 Interlocked.Add(ref _counter.SentMessages, 1);
//
//                                 // mark the bytes as consumed, and reset pending
//                                 consumed += pending;
//                                 pending = 0;
//                             }
//                             else
//                             {
//                                 // the entire command was not sent; decrement pending by
//                                 // the number of bytes from the command that was sent
//                                 pending += consumed - sentAndTrimmed;
//                                 break;
//                             }
//                         }
//
//                         if (consumed > 0)
//                         {
//                             // mark fully sent commands as consumed
//                             consumedPos = buffer.GetPosition(consumed);
//                             examinedOffset = sentAndTrimmed - consumed;
//                         }
//                         else
//                         {
//                             // no commands were consumed
//                             examinedOffset += sentAndTrimmed;
//                         }
//
//                         // lop off sent bytes for next iteration
//                         examinedPos = buffer.GetPosition(sentAndTrimmed);
//                         buffer = buffer.Slice(sentAndTrimmed);
//                     }
//                 }
//                 finally
//                 {
//                     _pipeReader.AdvanceTo(consumedPos, examinedPos);
//                 }
//
//                 if (result.IsCompleted)
//                 {
//                     break;
//                 }
//             }
//         }
//         catch (OperationCanceledException)
//         {
//             // ignore, intentionally disposed
//         }
//         catch (SocketClosedException)
//         {
//             // ignore, will be handled in read loop
//         }
//         catch (Exception ex)
//         {
//             logger.LogError(ex, "Unexpected error occured in write loop");
//             throw;
//         }
//
//         logger.LogDebug("WriteLoop finished.");
//     }
// }
