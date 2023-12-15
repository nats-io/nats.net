using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

internal sealed class NatsPipeliningWriteProtocolProcessor : IAsyncDisposable
{
    private readonly ISocketConnection _socketConnection;
    private readonly WriterState _state;
    private readonly ObjectPool _pool;
    private readonly ConnectionStatsCounter _counter;
    private readonly PipeReader _pipeReader;
    private readonly PipeWriter _pipeWriter;
    private readonly Channel<ICommand> _channel;
    private readonly NatsOpts _opts;
    private Task _readLoop;
    private readonly Task _writeLoop;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly CancellationTokenSource _cancellationTokenSource;
    private int _disposed;

    public NatsPipeliningWriteProtocolProcessor(ISocketConnection socketConnection, WriterState state, ObjectPool pool, ConnectionStatsCounter counter)
    {
        _socketConnection = socketConnection;
        _state = state;
        _pool = pool;
        _counter = counter;
        _pipeReader = state.PipeReader;
        _pipeWriter = state.PipeWriter;
        _channel = state.CommandBuffer;
        _opts = state.Opts;
        _cancellationTokenSource = new CancellationTokenSource();
        _readLoop = Task.CompletedTask;
        _writeLoop = Task.Run(WriteLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
#if NET6_0
            _cancellationTokenSource.Cancel();
#else
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
#endif
            await _writeLoop.ConfigureAwait(false); // wait for drain writer
            await _readLoop.ConfigureAwait(false); // wait for drain reader
        }
    }

    private async Task WriteLoopAsync()
    {
        var reader = _channel.Reader;
        var protocolWriter = new ProtocolWriter(_pipeWriter);
        var logger = _opts.LoggerFactory.CreateLogger<NatsPipeliningWriteProtocolProcessor>();
        var promiseList = new List<IPromise>(100);
        var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);
        var cancellationToken = _cancellationTokenSource.Token;

        try
        {
            // at first, send priority lane(initial command).
            {
                var firstCommands = _state.PriorityCommands;
                if (firstCommands.Count != 0)
                {
                    var count = firstCommands.Count;
                    var tempPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
                    var tempWriter = new ProtocolWriter(tempPipe.Writer);
                    foreach (var command in firstCommands)
                    {
                        command.Write(tempWriter);
                        await tempPipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                        if (command is IPromise p)
                        {
                            promiseList.Add(p);
                        }

                        command.Return(_pool); // Promise does not Return but set ObjectPool here.
                    }

                    await tempPipe.Writer.CompleteAsync().ConfigureAwait(false);
                    _state.PriorityCommands.Clear();

                    try
                    {
                        while (true)
                        {
                            var result = await tempPipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                            if (result.Buffer.Length > 0)
                            {
                                _stopwatch.Restart();
                                var sent = await _socketConnection.SendAsync(result.Buffer.First).ConfigureAwait(false);
                                _stopwatch.Stop();
                                if (isEnabledTraceLogging)
                                {
                                    logger.LogTrace("Socket.SendAsync. Size: {0}  Elapsed: {1}ms", sent, _stopwatch.Elapsed.TotalMilliseconds);
                                }

                                Interlocked.Add(ref _counter.SentBytes, sent);
                                tempPipe.Reader.AdvanceTo(result.Buffer.GetPosition(sent));
                            }

                            if (result.IsCompleted || result.IsCanceled)
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _socketConnection.SignalDisconnected(ex);
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
            promiseList.AddRange(_state.PendingPromises);
            _state.PendingPromises.Clear();

            // start read loop
            _readLoop = Task.Run(ReadLoopAsync);

            // main writer loop
            await foreach (var command in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var count = 0;
                    Interlocked.Decrement(ref _counter.PendingMessages);
                    if (command.IsCanceled)
                    {
                        continue;
                    }

                    try
                    {
                        if (command is IBatchCommand batch)
                        {
                            count += batch.Write(protocolWriter);
                        }
                        else
                        {
                            command.Write(protocolWriter);
                            count++;
                        }

                        await _pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
                        Interlocked.Add(ref _counter.SentMessages, count);

                        if (command is IPromise promise)
                        {
                            promise.SetResult();
                        }
                    }
                    catch (Exception e)
                    {
                        // flag potential serialization exceptions
                        if (command is IPromise promise)
                        {
                            promise.SetException(e);
                        }

                        throw;
                    }

                    command.Return(_pool); // Promise does not Return but set ObjectPool here.
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
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogDebug("WriteLoop finished.");
    }

    private async Task ReadLoopAsync()
    {
        var logger = _opts.LoggerFactory.CreateLogger<NatsPipeliningWriteProtocolProcessor>();
        var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);
        var cancellationToken = _cancellationTokenSource.Token;

        try
        {
            while (true)
            {
                var result = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (result.Buffer.Length > 0)
                {
                    _stopwatch.Restart();
                    var sent = await _socketConnection.SendAsync(result.Buffer.First).ConfigureAwait(false);
                    _stopwatch.Stop();
                    if (isEnabledTraceLogging)
                    {
                        logger.LogTrace("Socket.SendAsync. Size: {0}  Elapsed: {1}ms", sent, _stopwatch.Elapsed.TotalMilliseconds);
                    }

                    Interlocked.Add(ref _counter.SentBytes, sent);
                    _pipeReader.AdvanceTo(result.Buffer.GetPosition(sent));
                }

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogDebug("ReadLoopAsync finished.");
    }
}
