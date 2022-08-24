using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace AlterNats;

public enum NatsConnectionState
{
    Closed,
    Open,
    Connecting,
    Reconnecting,
}

// This writer state is reused when reconnecting.
internal sealed class WriterState
{
    public FixedArrayBufferWriter BufferWriter { get; }
    public Channel<ICommand> CommandBuffer { get; }
    public NatsOptions Options { get; }
    public List<ICommand> PriorityCommands { get; }
    public List<IPromise> PendingPromises { get; }

    public WriterState(NatsOptions options)
    {
        Options = options;
        BufferWriter = new FixedArrayBufferWriter();
        CommandBuffer = Channel.CreateUnbounded<ICommand>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false, // always should be in async loop.
            SingleWriter = false,
            SingleReader = true,
        });
        PriorityCommands = new List<ICommand>();
        PendingPromises = new List<IPromise>();
    }
}

public partial class NatsConnection : IAsyncDisposable, INatsCommand
{
    readonly object gate = new object();
    readonly WriterState writerState;
    readonly ChannelWriter<ICommand> commandWriter;
    readonly SubscriptionManager subscriptionManager;
    readonly RequestResponseManager requestResponseManager;
    readonly ILogger<NatsConnection> logger;
    readonly ObjectPool pool;
    readonly string name;
    internal readonly ConnectionStatsCounter counter; // allow to call from external sources
    internal readonly ReadOnlyMemory<byte> indBoxPrefix;

    int pongCount;
    bool isDisposed;

    // when reconnect, make new instance.
    ISocketConnection? socket;
    CancellationTokenSource? pingTimerCancellationTokenSource;
    NatsUri? currentConnectUri;
    NatsReadProtocolProcessor? socketReader;
    NatsPipeliningWriteProtocolProcessor? socketWriter;
    TaskCompletionSource waitForOpenConnection;

    public NatsOptions Options { get; }
    public NatsConnectionState ConnectionState { get; private set; }
    public ServerInfo? ServerInfo { get; internal set; } // server info is set when received INFO

    /// <summary>
    /// Hook before TCP connection open.
    /// </summary>
    public Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync;

    // events
    public event EventHandler<string>? ConnectionDisconnected;
    public event EventHandler<string>? ConnectionOpened;
    public event EventHandler<string>? ReconnectFailed;

    public NatsConnection()
        : this(NatsOptions.Default)
    {
    }

    public NatsConnection(NatsOptions options)
    {
        this.Options = options;
        this.ConnectionState = NatsConnectionState.Closed;
        this.waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pool = new ObjectPool(options.CommandPoolSize);
        this.name = options.ConnectOptions.Name ?? "";
        this.counter = new ConnectionStatsCounter();
        this.writerState = new WriterState(options);
        this.commandWriter = writerState.CommandBuffer.Writer;
        this.subscriptionManager = new SubscriptionManager(this);
        this.requestResponseManager = new RequestResponseManager(this, pool);
        this.indBoxPrefix = Encoding.ASCII.GetBytes($"{options.InboxPrefix}{Guid.NewGuid()}.");
        this.logger = options.LoggerFactory.CreateLogger<NatsConnection>();
    }

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public async ValueTask ConnectAsync()
    {
        if (this.ConnectionState == NatsConnectionState.Open) return;

        TaskCompletionSource? waiter = null;
        lock (gate)
        {
            ThrowIfDisposed();
            if (ConnectionState != NatsConnectionState.Closed)
            {
                waiter = waitForOpenConnection;
            }
            else
            {
                // when closed, change state to connecting and only first connection try-to-connect.
                ConnectionState = NatsConnectionState.Connecting;
            }
        }

        if (waiter != null)
        {
            await waiter.Task.ConfigureAwait(false);
            return;
        }
        else
        {
            // Only Closed(initial) state, can run initial connect.
            await InitialConnectAsync().ConfigureAwait(false);
        }
    }

    public NatsStats GetStats() => counter.ToStats();

    async ValueTask InitialConnectAsync()
    {
        Debug.Assert(ConnectionState == NatsConnectionState.Connecting);

        var uris = Options.GetSeedUris();
        foreach (var uri in uris)
        {
            try
            {
                var target = (uri.Host, uri.Port);
                if (OnConnectingAsync != null)
                {
                    logger.LogInformation("Try to invoke OnConnectingAsync before connect to NATS.");
                    target = await OnConnectingAsync(target).ConfigureAwait(false);
                }

                logger.LogInformation("Try to connect NATS {0}", uri);
                if (uri.IsWebSocket)
                {
                    var conn = new WebSocketConnection();
                    await conn.ConnectAsync(uri.Uri, Options.ConnectTimeout).ConfigureAwait(false);
                    this.socket = conn;
                }
                else
                {
                    var conn = new TcpConnection();
                    await conn.ConnectAsync(target.Host, target.Port, Options.ConnectTimeout).ConfigureAwait(false);
                    this.socket = conn;
                }

                this.currentConnectUri = uri;
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fail to connect NATS {0}", uri);
            }
        }
        if (this.socket == null)
        {
            var exception = new NatsException("can not connect uris: " + String.Join(",", uris.Select(x => x.ToString())));
            lock (gate)
            {
                this.ConnectionState = NatsConnectionState.Closed; // allow retry connect
                waitForOpenConnection.TrySetException(exception); // throw for waiter
                this.waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            throw exception;
        }

        // Connected completely but still ConnectionState is Connecting(require after receive INFO).

        // add CONNECT and PING command to priority lane
        var connectCommand = AsyncConnectCommand.Create(pool, Options.ConnectOptions);
        writerState.PriorityCommands.Add(connectCommand);
        writerState.PriorityCommands.Add(PingCommand.Create(pool));

        // Run Reader/Writer LOOP start
        var waitForInfoSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        this.socketWriter = new NatsPipeliningWriteProtocolProcessor(socket, writerState, pool, counter);
        this.socketReader = new NatsReadProtocolProcessor(socket, this, waitForInfoSignal, waitForPongOrErrorSignal);

        try
        {
            // before send connect, wait INFO.
            await waitForInfoSignal.Task.ConfigureAwait(false);
            // send COMMAND and PING
            await connectCommand.AsValueTask().ConfigureAwait(false);
            // receive COMMAND response(PONG or ERROR)
            await waitForPongOrErrorSignal.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // can not start reader/writer
            var uri = currentConnectUri;

            await socketWriter!.DisposeAsync().ConfigureAwait(false);
            await socketReader!.DisposeAsync().ConfigureAwait(false);
            await socket!.DisposeAsync().ConfigureAwait(false);
            socket = null;
            socketWriter = null;
            socketReader = null;
            currentConnectUri = null;

            var exception = new NatsException("can not start to connect nats server: " + uri, ex);
            lock (gate)
            {
                this.ConnectionState = NatsConnectionState.Closed; // allow retry connect
                waitForOpenConnection.TrySetException(exception); // throw for waiter
                this.waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            throw exception;
        }

        lock (gate)
        {
            var url = currentConnectUri;
            logger.LogInformation("Connect succeed {0}, NATS {1}", name, url);
            this.ConnectionState = NatsConnectionState.Open;
            this.pingTimerCancellationTokenSource = new CancellationTokenSource();
            StartPingTimer(pingTimerCancellationTokenSource.Token);
            this.waitForOpenConnection.TrySetResult();
            Task.Run(ReconnectLoop);
            ConnectionOpened?.Invoke(this, url?.ToString() ?? "");
        }
    }

    async void ReconnectLoop()
    {
        try
        {
            // If dispose this client, WaitForClosed throws OperationCanceledException so stop reconnect-loop correctly.
            await socket!.WaitForClosed.ConfigureAwait(false);

            logger.LogTrace($"Detect connection {name} closed, start to cleanup current connection and start to reconnect.");
            lock (gate)
            {
                this.ConnectionState = NatsConnectionState.Reconnecting;
                this.waitForOpenConnection.TrySetCanceled();
                this.waitForOpenConnection = new TaskCompletionSource();
                this.pingTimerCancellationTokenSource?.Cancel();
                this.requestResponseManager.Reset();
            }

            // Invoke after state changed
            ConnectionDisconnected?.Invoke(this, currentConnectUri?.ToString() ?? "");

            // Cleanup current reader/writer
            {
                // reader is not share state, can dispose asynchronously.
                var reader = socketReader!;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await reader.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error occured when disposing socket reader loop.");
                    }
                });

                // writer's internal buffer/channel is not thread-safe, must wait complete.
                await socketWriter!.DisposeAsync().ConfigureAwait(false);
            }

            // Dispose current and create new
            await socket.DisposeAsync().ConfigureAwait(false);

            NatsUri[] urls;
            var defaultScheme = currentConnectUri?.Uri.Scheme ?? NatsUri.DefaultScheme;
            if (Options.NoRandomize)
            {
                urls = this.ServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, defaultScheme)).Distinct().ToArray() ?? Array.Empty<NatsUri>();
                if (urls.Length == 0)
                {
                    urls = Options.GetSeedUris();
                }
            }
            else
            {
                urls = this.ServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, defaultScheme)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray() ?? Array.Empty<NatsUri>();
                if (urls.Length == 0)
                {
                    urls = Options.GetSeedUris();
                }
            }

            if (this.currentConnectUri != null)
            {
                // add last.
                urls = urls.Where(x => x != currentConnectUri).Append(currentConnectUri).ToArray();
            }

            currentConnectUri = null;
            var urlEnumerator = urls.AsEnumerable().GetEnumerator();
            NatsUri? url = null;
        CONNECT_AGAIN:
            try
            {
                if (urlEnumerator.MoveNext())
                {
                    url = urlEnumerator.Current;
                    var target = (url.Host, url.Port);
                    if (OnConnectingAsync != null)
                    {
                        logger.LogInformation("Try to invoke OnConnectingAsync before connect to NATS.");
                        target = await OnConnectingAsync(target).ConfigureAwait(false);
                    }

                    logger.LogInformation("Try to connect NATS {0}", url);
                    if (url.IsWebSocket)
                    {
                        var conn = new WebSocketConnection();
                        await conn.ConnectAsync(url.Uri, Options.ConnectTimeout).ConfigureAwait(false);
                        this.socket = conn;
                    }
                    else
                    {
                        var conn = new TcpConnection();
                        await conn.ConnectAsync(target.Host, target.Port, Options.ConnectTimeout).ConfigureAwait(false);
                        this.socket = conn;
                    }

                    this.currentConnectUri = url;
                }
                else
                {
                    urlEnumerator = urls.AsEnumerable().GetEnumerator();
                    goto CONNECT_AGAIN;
                }

                // add CONNECT and PING command to priority lane
                var connectCommand = AsyncConnectCommand.Create(pool, Options.ConnectOptions);
                writerState.PriorityCommands.Add(connectCommand);
                writerState.PriorityCommands.Add(PingCommand.Create(pool));

                // Add SUBSCRIBE command to priority lane
                var subscribeCommand = AsyncSubscribeBatchCommand.Create(pool, subscriptionManager.GetExistingSubscriptions());
                writerState.PriorityCommands.Add(subscribeCommand);

                // Run Reader/Writer LOOP start
                var waitForInfoSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                this.socketWriter = new NatsPipeliningWriteProtocolProcessor(socket, writerState, pool, counter);
                this.socketReader = new NatsReadProtocolProcessor(socket, this, waitForInfoSignal, waitForPongOrErrorSignal);

                await waitForInfoSignal.Task.ConfigureAwait(false);
                await connectCommand.AsValueTask().ConfigureAwait(false);
                await waitForPongOrErrorSignal.Task.ConfigureAwait(false);
                await subscribeCommand.AsValueTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (url != null)
                {
                    logger.LogError(ex, "Fail to connect NATS {0}", url);
                }

                if (socketWriter != null)
                {
                    await socketWriter.DisposeAsync().ConfigureAwait(false);
                }
                if (socketReader != null)
                {
                    await socketReader.DisposeAsync().ConfigureAwait(false);
                }
                if (socket != null)
                {
                    await socket.DisposeAsync().ConfigureAwait(false);
                }
                socket = null;
                socketWriter = null;
                socketReader = null;
                writerState.PriorityCommands.Clear();

                ReconnectFailed?.Invoke(this, url?.ToString() ?? "");
                await WaitWithJitterAsync().ConfigureAwait(false);
                goto CONNECT_AGAIN;
            }

            lock (gate)
            {
                logger.LogInformation("Connect succeed {0}, NATS {1}", name, url);
                this.ConnectionState = NatsConnectionState.Open;
                this.pingTimerCancellationTokenSource = new CancellationTokenSource();
                StartPingTimer(pingTimerCancellationTokenSource.Token);
                this.waitForOpenConnection.TrySetResult();
                Task.Run(ReconnectLoop);
                ConnectionOpened?.Invoke(this, url?.ToString() ?? "");
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException) return;
            logger.LogError(ex, "Unknown error, loop stopped and connection is invalid state.");
        }
    }

    async Task WaitWithJitterAsync()
    {
        var jitter = Random.Shared.NextDouble() * Options.ReconnectJitter.TotalMilliseconds;
        var waitTime = Options.ReconnectWait + TimeSpan.FromMilliseconds(jitter);
        if (waitTime != TimeSpan.Zero)
        {
            logger.LogTrace("Wait {0}ms to reconnect.", waitTime.TotalMilliseconds);
            await Task.Delay(waitTime).ConfigureAwait(false);
        }
    }

    async void StartPingTimer(CancellationToken cancellationToken)
    {
        if (Options.PingInterval == TimeSpan.Zero) return;

        var periodicTimer = new PeriodicTimer(Options.PingInterval);
        ResetPongCount();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref pongCount) > Options.MaxPingOut)
                {
                    logger.LogInformation("Detect MaxPingOut, try to connection abort.");
                    if (socket != null)
                    {
                        await socket.AbortConnectionAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                PostPing();
                await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch { }
    }

    internal void EnqueuePing(AsyncPingCommand pingCommand)
    {
        // Enqueue Ping Command to current working reader.
        var reader = socketReader;
        if (reader != null)
        {
            if (reader.TryEnqueuePing(pingCommand))
            {
                return;
            }
        }
        // Can not add PING, set fail.
        pingCommand.SetCanceled(CancellationToken.None);
    }

    // internal commands.

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void EnqueueCommand(ICommand command)
    {
        if (commandWriter.TryWrite(command))
        {
            Interlocked.Increment(ref counter.PendingMessages);
        }
    }

    internal void PostPong()
    {
        EnqueueCommand(PongCommand.Create(pool));
    }

    internal ValueTask SubscribeAsync(int subscriptionId, string subject, in NatsKey? queueGroup)
    {
        var command = AsyncSubscribeCommand.Create(pool, subscriptionId, new NatsKey(subject, true), queueGroup);
        EnqueueCommand(command);
        return command.AsValueTask();
    }

    internal void PostUnsubscribe(int subscriptionId)
    {
        EnqueueCommand(UnsubscribeCommand.Create(pool, subscriptionId));
    }

    internal void PostCommand(ICommand command)
    {
        EnqueueCommand(command);
    }

    internal void PublishToClientHandlers(int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToClientHandlers(subscriptionId, buffer);
    }

    internal void PublishToRequestHandler(int subscriptionId, in NatsKey replyTo, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToRequestHandler(subscriptionId, replyTo, buffer);
    }

    internal void PublishToResponseHandler(int requestId, in ReadOnlySequence<byte> buffer)
    {
        requestResponseManager.PublishToResponseHandler(requestId, buffer);
    }

    internal void ResetPongCount()
    {
        Interlocked.Exchange(ref pongCount, 0);
    }

    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            logger.Log(LogLevel.Information, $"Disposing connection {name}.");

            // Dispose Writer(Drain prepared queues -> write to socket)
            // Close Socket
            // Dispose Reader(Drain read buffers but no reads more)
            if (socketWriter != null)
            {
                await socketWriter.DisposeAsync().ConfigureAwait(false);
            }
            if (socket != null)
            {
                await socket.DisposeAsync().ConfigureAwait(false);
            }
            if (socketReader != null)
            {
                await socketReader.DisposeAsync().ConfigureAwait(false);
            }
            if (pingTimerCancellationTokenSource != null)
            {
                pingTimerCancellationTokenSource.Cancel();
            }
            foreach (var item in writerState.PendingPromises)
            {
                item.SetCanceled(CancellationToken.None);
            }
            subscriptionManager.Dispose();
            requestResponseManager.Dispose();
            waitForOpenConnection.TrySetCanceled();
        }
    }

    void ThrowIfDisposed()
    {
        if (isDisposed) throw new ObjectDisposedException(null);
    }

    async void WithConnect(Action<NatsConnection> core)
    {
        try
        {
            await ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // log will shown on ConnectAsync failed
            return;
        }
        core(this);
    }

    async void WithConnect<T1>(T1 item1, Action<NatsConnection, T1> core)
    {
        try
        {
            await ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // log will shown on ConnectAsync failed
            return;
        }
        core(this, item1);
    }

    async void WithConnect<T1, T2>(T1 item1, T2 item2, Action<NatsConnection, T1, T2> core)
    {
        try
        {
            await ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // log will shown on ConnectAsync failed
            return;
        }
        core(this, item1, item2);
    }

    async ValueTask WithConnectAsync(Func<NatsConnection, ValueTask> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        await coreAsync(this).ConfigureAwait(false);
    }

    async ValueTask WithConnectAsync<T1>(T1 item1, Func<NatsConnection, T1, ValueTask> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        await coreAsync(this, item1).ConfigureAwait(false);
    }

    async ValueTask WithConnectAsync<T1, T2>(T1 item1, T2 item2, Func<NatsConnection, T1, T2, ValueTask> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        await coreAsync(this, item1, item2).ConfigureAwait(false);
    }

    async ValueTask<T> WithConnectAsync<T>(Func<NatsConnection, ValueTask<T>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this).ConfigureAwait(false);
    }

    async ValueTask<TResult> WithConnectAsync<T1, T2, TResult>(T1 item1, T2 item2, Func<NatsConnection, T1, T2, ValueTask<TResult>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this, item1, item2).ConfigureAwait(false);
    }

    async ValueTask<TResult> WithConnectAsync<T1, T2, T3, TResult>(T1 item1, T2 item2, T3 item3, Func<NatsConnection, T1, T2, T3, ValueTask<TResult>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this, item1, item2, item3).ConfigureAwait(false);
    }
}
