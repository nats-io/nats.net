using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace AlterNats;

internal sealed class NatsPipeliningWriteProtocolProcessor : IAsyncDisposable
{
    readonly ISocketConnection socketConnection;
    readonly WriterState state;
    readonly ObjectPool pool;
    readonly ConnectionStatsCounter counter;
    readonly FixedArrayBufferWriter bufferWriter;
    readonly Channel<ICommand> channel;
    readonly NatsOptions options;
    readonly Task writeLoop;
    readonly Stopwatch stopwatch = new Stopwatch();
    readonly CancellationTokenSource cancellationTokenSource;
    int disposed;

    public NatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection, WriterState state, ObjectPool pool, ConnectionStatsCounter counter)
    {
        this.socketConnection = socketConnection;
        this.state = state;
        this.pool = pool;
        this.counter = counter;
        this.bufferWriter = state.BufferWriter;
        this.channel = state.CommandBuffer;
        this.options = state.Options;
        this.cancellationTokenSource = new CancellationTokenSource();
        this.writeLoop = Task.Run(WriteLoopAsync);
    }

    async Task WriteLoopAsync()
    {
        var reader = channel.Reader;
        var protocolWriter = new ProtocolWriter(bufferWriter);
        var logger = options.LoggerFactory.CreateLogger<NatsPipeliningWriteProtocolProcessor>();
        var writerBufferSize = options.WriterBufferSize;
        var promiseList = new List<IPromise>(100);
        var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);

        try
        {
            // at first, send priority lane(initial command).
            {
                var firstCommands = state.PriorityCommands;
                if (firstCommands.Count != 0)
                {
                    var count = firstCommands.Count;
                    var tempBuffer = new FixedArrayBufferWriter(1024);
                    var tempWriter = new ProtocolWriter(tempBuffer);
                    foreach (var command in firstCommands)
                    {
                        command.Write(tempWriter);

                        if (command is IPromise p)
                        {
                            promiseList.Add(p);
                        }

                        command.Return(pool); // Promise does not Return but set ObjectPool here.
                    }
                    state.PriorityCommands.Clear();

                    try
                    {
                        var memory = tempBuffer.WrittenMemory;
                        while (memory.Length > 0)
                        {
                            stopwatch.Restart();
                            var sent = await socketConnection.SendAsync(memory).ConfigureAwait(false);
                            stopwatch.Stop();
                            if (isEnabledTraceLogging)
                            {
                                logger.LogTrace("Socket.SendAsync. Size: {0} BatchSize: {1} Elapsed: {2}ms", sent, count, stopwatch.Elapsed.TotalMilliseconds);
                            }
                            Interlocked.Add(ref counter.SentBytes, sent);
                            memory = memory.Slice(sent);
                        }
                    }
                    catch (Exception ex)
                    {
                        socketConnection.SignalDisconnected(ex);
                        foreach (var item in promiseList)
                        {
                            item.SetException(ex); // signal failed
                        }
                        return; // when socket closed, finish writeloop.
                    }

                    foreach (var item in promiseList)
                    {
                        item.SetResult();
                    }
                    promiseList.Clear();
                }
            }

            // restore promise(command is exist in bufferWriter) when enter from reconnecting.
            promiseList.AddRange(state.PendingPromises);
            state.PendingPromises.Clear();

            // main writer loop
            while ((bufferWriter.WrittenCount != 0) || (await reader.WaitToReadAsync(cancellationTokenSource.Token).ConfigureAwait(false)))
            {
                try
                {
                    var count = 0;
                    while (bufferWriter.WrittenCount < writerBufferSize && reader.TryRead(out var command))
                    {
                        Interlocked.Decrement(ref counter.PendingMessages);

                        if (command is IBatchCommand batch)
                        {
                            count += batch.Write(protocolWriter);
                        }
                        else
                        {
                            command.Write(protocolWriter);
                            count++;
                        }

                        if (command is IPromise p)
                        {
                            promiseList.Add(p);
                        }

                        command.Return(pool); // Promise does not Return but set ObjectPool here.
                    }

                    try
                    {
                        // SendAsync(ReadOnlyMemory) is very efficient, internally using AwaitableAsyncSocketEventArgs
                        // should use cancellation token?, currently no, wait for flush complete.
                        var memory = bufferWriter.WrittenMemory;
                        while (memory.Length != 0)
                        {
                            stopwatch.Restart();
                            var sent = await socketConnection.SendAsync(memory).ConfigureAwait(false);
                            stopwatch.Stop();
                            if (isEnabledTraceLogging)
                            {
                                logger.LogTrace("Socket.SendAsync. Size: {0} BatchSize: {1} Elapsed: {2}ms", sent, count, stopwatch.Elapsed.TotalMilliseconds);
                            }
                            if (sent == 0)
                            {
                                throw new SocketClosedException(null);
                            }
                            Interlocked.Add(ref counter.SentBytes, sent);

                            memory = memory.Slice(sent);
                        }
                        Interlocked.Add(ref counter.SentMessages, count);

                        bufferWriter.Reset();
                        foreach (var item in promiseList)
                        {
                            item.SetResult();
                        }
                        promiseList.Clear();
                    }
                    catch (Exception ex) // may receive from socket.SendAsync
                    {
                        // when error, command is dequeued and written buffer is still exists in state.BufferWriter
                        // store current pending promises to state.
                        state.PendingPromises.AddRange(promiseList);
                        socketConnection.SignalDisconnected(ex);
                        return; // when socket closed, finish writeloop.
                    }
                }
                catch (Exception ex)
                {
                    if (ex is SocketClosedException)
                    {
                        return;
                    }
                    try
                    {
                        logger.LogError(ex, "Internal error occured on WriteLoop.");
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                if (bufferWriter.WrittenMemory.Length != 0)
                {
                    await socketConnection.SendAsync(bufferWriter.WrittenMemory).ConfigureAwait(false);
                }
            }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref disposed) == 1)
        {
            cancellationTokenSource.Cancel();
            await writeLoop.ConfigureAwait(false); // wait for drain writer
        }
    }
}
