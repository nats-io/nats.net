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
    readonly TimeSpan socketComponentDisposeTimeout = TimeSpan.FromSeconds(5);
    internal readonly ConnectionStatsCounter counter; // allow to call from external sources
    internal readonly ReadOnlyMemory<byte> inboxPrefix;

    int pongCount;
    bool isDisposed;

    // when reconnect, make new instance.
    ISocketConnection? socket;
    CancellationTokenSource? pingTimerCancellationTokenSource;
    NatsUri? currentConnectUri;
    NatsUri? lastSeedConnectUri;
    NatsReadProtocolProcessor? socketReader;
    NatsPipeliningWriteProtocolProcessor? socketWriter;
    TaskCompletionSource waitForOpenConnection;
    TlsCerts? tlsCerts;

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
        Options = options;
        ConnectionState = NatsConnectionState.Closed;
        waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool = new ObjectPool(options.CommandPoolSize);
        name = options.ConnectOptions.Name ?? "";
        counter = new ConnectionStatsCounter();
        writerState = new WriterState(options);
        commandWriter = writerState.CommandBuffer.Writer;
        subscriptionManager = new SubscriptionManager(this);
        requestResponseManager = new RequestResponseManager(this, pool);
        inboxPrefix = Encoding.ASCII.GetBytes($"{options.InboxPrefix}{Guid.NewGuid()}.");
        logger = options.LoggerFactory.CreateLogger<NatsConnection>();
    }

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public async ValueTask ConnectAsync()
    {
        if (ConnectionState == NatsConnectionState.Open) return;

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
        if (Options.TlsOptions.Disabled && uris.Any(u => u.IsTls))
            throw new NatsException($"URI {uris.First(u => u.IsTls)} requires TLS but TlsOptions.Disabled is set to true");
        if (Options.TlsOptions.Required)
            tlsCerts = new TlsCerts(Options.TlsOptions);

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
                    socket = conn;
                }
                else
                {
                    var conn = new TcpConnection();
                    await conn.ConnectAsync(target.Host, target.Port, Options.ConnectTimeout).ConfigureAwait(false);
                    socket = conn;
                }

                currentConnectUri = uri;
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fail to connect NATS {0}", uri);
            }
        }
        if (socket == null)
        {
            var exception = new NatsException("can not connect uris: " + String.Join(",", uris.Select(x => x.ToString())));
            lock (gate)
            {
                ConnectionState = NatsConnectionState.Closed; // allow retry connect
                waitForOpenConnection.TrySetException(exception); // throw for waiter
                waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            throw exception;
        }

        // Connected completely but still ConnectionState is Connecting(require after receive INFO).
        try
        {
            await SetupReaderWriterAsync(false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var uri = currentConnectUri;
            currentConnectUri = null;
            var exception = new NatsException("can not start to connect nats server: " + uri, ex);
            lock (gate)
            {
                ConnectionState = NatsConnectionState.Closed; // allow retry connect
                waitForOpenConnection.TrySetException(exception); // throw for waiter
                waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            throw exception;
        }

        lock (gate)
        {
            var url = currentConnectUri;
            logger.LogInformation("Connect succeed {0}, NATS {1}", name, url);
            ConnectionState = NatsConnectionState.Open;
            pingTimerCancellationTokenSource = new CancellationTokenSource();
            StartPingTimer(pingTimerCancellationTokenSource.Token);
            waitForOpenConnection.TrySetResult();
            _ = Task.Run(ReconnectLoop);
            ConnectionOpened?.Invoke(this, url?.ToString() ?? "");
        }
    }

    async ValueTask SetupReaderWriterAsync(bool reconnect)
    {
        if (currentConnectUri!.IsSeed)
            lastSeedConnectUri = currentConnectUri;

        // add CONNECT and PING command to priority lane
        writerState.PriorityCommands.Clear();
        var connectCommand = AsyncConnectCommand.Create(pool, Options.ConnectOptions);
        writerState.PriorityCommands.Add(connectCommand);
        writerState.PriorityCommands.Add(PingCommand.Create(pool));

        if (reconnect)
        {
            // Add SUBSCRIBE command to priority lane
            var subscribeCommand =
                AsyncSubscribeBatchCommand.Create(pool, subscriptionManager.GetExistingSubscriptions());
            writerState.PriorityCommands.Add(subscribeCommand);
        }

        // create the socket reader
        var waitForInfoSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        socketReader = new NatsReadProtocolProcessor(socket!, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);

        try
        {
            // wait for INFO
            await waitForInfoSignal.Task.ConfigureAwait(false);

            // check to see if we should upgrade to TLS
            if (socket is TcpConnection tcpConnection)
            {
                if (Options.TlsOptions.Disabled && ServerInfo!.TlsRequired)
                    throw new NatsException(
                        $"Server {currentConnectUri} requires TLS but TlsOptions.Disabled is set to true");

                if (Options.TlsOptions.Required && !ServerInfo!.TlsRequired && !ServerInfo.TlsAvailable)
                    throw new NatsException(
                        $"Server {currentConnectUri} does not support TLS but TlsOptions.Disabled is set to true");

                if (Options.TlsOptions.Required || ServerInfo!.TlsRequired || ServerInfo.TlsAvailable)
                {
                    // do TLS upgrade
                    // if the current URI is not a seed URI and is not a DNS hostname, check the server cert against the
                    // last seed hostname if it was a DNS hostname
                    var targetHost = currentConnectUri.Host;
                    if (!currentConnectUri.IsSeed
                        && Uri.CheckHostName(targetHost) != UriHostNameType.Dns
                        && Uri.CheckHostName(lastSeedConnectUri!.Host) == UriHostNameType.Dns)
                    {
                        targetHost = lastSeedConnectUri.Host;
                    }

                    logger.LogDebug("Perform TLS Upgrade to " + targetHost);

                    // cancel INFO parsed signal and dispose current socket reader
                    infoParsedSignal.SetCanceled();
                    await socketReader!.DisposeAsync().ConfigureAwait(false);
                    socketReader = null;

                    // upgrade TcpConnection to SslConnection
                    var sslConnection = tcpConnection.UpgradeToSslStreamConnection(Options.TlsOptions, tlsCerts);
                    await sslConnection.AuthenticateAsClientAsync(targetHost).ConfigureAwait(false);
                    socket = sslConnection;

                    // create new socket reader
                    waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    socketReader = new NatsReadProtocolProcessor(socket, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);
                }
            }

            // mark INFO as parsed
            infoParsedSignal.SetResult();

            // create the socket writer
            socketWriter = new NatsPipeliningWriteProtocolProcessor(socket!, writerState, pool, counter);

            // wait for COMMAND to send
            await connectCommand.AsValueTask().ConfigureAwait(false);
            // receive COMMAND response (PONG or ERROR)
            await waitForPongOrErrorSignal.Task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            infoParsedSignal.TrySetCanceled();
            await DisposeSocketAsync(true).ConfigureAwait(false);
            throw;
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
                ConnectionState = NatsConnectionState.Reconnecting;
                waitForOpenConnection.TrySetCanceled();
                waitForOpenConnection = new TaskCompletionSource();
                pingTimerCancellationTokenSource?.Cancel();
                requestResponseManager.Reset();
            }

            // Invoke after state changed
            ConnectionDisconnected?.Invoke(this, currentConnectUri?.ToString() ?? "");

            // Cleanup current socket
            await DisposeSocketAsync(true).ConfigureAwait(false);


            var defaultScheme = currentConnectUri!.Uri.Scheme;
            var urls = (Options.NoRandomize
                ? ServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, false, defaultScheme)).Distinct().ToArray()
                : ServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, false, defaultScheme)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray())
                    ?? Array.Empty<NatsUri>();
            if (urls.Length == 0)
                urls = Options.GetSeedUris();

            // add last.
            urls = urls.Where(x => x != currentConnectUri).Append(currentConnectUri).ToArray();

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
                        socket = conn;
                    }
                    else
                    {
                        var conn = new TcpConnection();
                        await conn.ConnectAsync(target.Host, target.Port, Options.ConnectTimeout).ConfigureAwait(false);
                        socket = conn;
                    }

                    currentConnectUri = url;
                }
                else
                {
                    urlEnumerator.Dispose();
                    urlEnumerator = urls.AsEnumerable().GetEnumerator();
                    goto CONNECT_AGAIN;
                }

                await SetupReaderWriterAsync(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (url != null)
                {
                    logger.LogError(ex, "Fail to connect NATS {0}", url);
                }

                ReconnectFailed?.Invoke(this, url?.ToString() ?? "");
                await WaitWithJitterAsync().ConfigureAwait(false);
                goto CONNECT_AGAIN;
            }

            lock (gate)
            {
                logger.LogInformation("Connect succeed {0}, NATS {1}", name, url);
                ConnectionState = NatsConnectionState.Open;
                pingTimerCancellationTokenSource = new CancellationTokenSource();
                StartPingTimer(pingTimerCancellationTokenSource.Token);
                waitForOpenConnection.TrySetResult();
                _ = Task.Run(ReconnectLoop);
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

    // catch and log all exceptions, enforcing the socketComponentDisposeTimeout
    async ValueTask DisposeSocketComponentAsync(IAsyncDisposable component, string description)
    {
        try
        {
            var dispose = component.DisposeAsync();
            if (!dispose.IsCompletedSuccessfully)
                await dispose.AsTask().WaitAsync(socketComponentDisposeTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error occured when disposing {description}.");
        }
    }

    // Dispose Writer(Drain prepared queues -> write to socket)
    // Close Socket
    // Dispose Reader(Drain read buffers but no reads more)
    async ValueTask DisposeSocketAsync(bool asyncReaderDispose)
    {
        // writer's internal buffer/channel is not thread-safe, must wait until complete.
        if (socketWriter != null)
        {
            await DisposeSocketComponentAsync(socketWriter, "socket writer").ConfigureAwait(false);
            socketWriter = null;
        }

        if (socket != null)
        {
            await DisposeSocketComponentAsync(socket, "socket").ConfigureAwait(false);
            socket = null;
        }

        if (socketReader != null)
        {
            if (asyncReaderDispose)
            {
                // reader is not share state, can dispose asynchronously.
                var reader = socketReader;
                _ = Task.Run(() => DisposeSocketComponentAsync(reader, "socket reader asynchronously"));
            }
            else
            {
                await DisposeSocketComponentAsync(socketReader, "socket reader").ConfigureAwait(false);
            }

            socketReader = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            logger.Log(LogLevel.Information, $"Disposing connection {name}.");

            await DisposeSocketAsync(false).ConfigureAwait(false);
            pingTimerCancellationTokenSource?.Cancel();
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
