using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public enum NatsConnectionState
{
    Closed,
    Open,
    Connecting,
    Reconnecting,
}

public partial class NatsConnection : IAsyncDisposable, INatsConnection
{
#pragma warning disable SA1401
    /// <summary>
    /// Hook before TCP connection open.
    /// </summary>
    public Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync;

    internal readonly ConnectionStatsCounter Counter; // allow to call from external sources
    internal ServerInfo? WritableServerInfo;
#pragma warning restore SA1401
    private readonly object _gate = new object();
    private readonly ILogger<NatsConnection> _logger;
    private readonly ObjectPool _pool;
    private readonly CancellationTimerPool _cancellationTimerPool;
    private readonly CancellationTokenSource _disposedCancellationTokenSource;
    private readonly string _name;
    private readonly TimeSpan _socketComponentDisposeTimeout = TimeSpan.FromSeconds(5);
    private readonly BoundedChannelOptions _defaultSubscriptionChannelOpts;

    private int _pongCount;
    private bool _isDisposed;
    private int _connectionState;

    // when reconnect, make new instance.
    private ISocketConnection? _socket;
    private CancellationTokenSource? _pingTimerCancellationTokenSource;
    private volatile NatsUri? _currentConnectUri;
    private volatile NatsUri? _lastSeedConnectUri;
    private NatsReadProtocolProcessor? _socketReader;
    private NatsPipeliningWriteProtocolProcessor? _socketWriter;
    private TaskCompletionSource _waitForOpenConnection;
    private TlsCerts? _tlsCerts;
    private ClientOpts _clientOpts;
    private UserCredentials? _userCredentials;
    private int _connectRetry;
    private TimeSpan _backoff = TimeSpan.Zero;
    private string _lastAuthError = string.Empty;
    private bool _stopRetries;

    public NatsConnection()
        : this(NatsOpts.Default)
    {
    }

    public NatsConnection(NatsOpts opts)
    {
        Opts = opts;
        ConnectionState = NatsConnectionState.Closed;
        _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _disposedCancellationTokenSource = new CancellationTokenSource();
        _pool = new ObjectPool(opts.ObjectPoolSize);
        _cancellationTimerPool = new CancellationTimerPool(_pool, _disposedCancellationTokenSource.Token);
        _name = opts.Name;
        Counter = new ConnectionStatsCounter();
        CommandWriter = new CommandWriter(Opts, Counter);
        InboxPrefix = NewInbox(opts.InboxPrefix);
        SubscriptionManager = new SubscriptionManager(this, InboxPrefix);
        _logger = opts.LoggerFactory.CreateLogger<NatsConnection>();
        _clientOpts = ClientOpts.Create(Opts);
        HeaderParser = new NatsHeaderParser(opts.HeaderEncoding);
        _defaultSubscriptionChannelOpts = new BoundedChannelOptions(opts.SubPendingChannelCapacity)
        {
            FullMode = opts.SubPendingChannelFullMode,
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        };
    }

    // events
    public event EventHandler<string>? ConnectionDisconnected;

    public event EventHandler<string>? ConnectionOpened;

    public event EventHandler<string>? ReconnectFailed;

    public event EventHandler<INatsError>? OnError;

    public NatsOpts Opts { get; }

    public NatsConnectionState ConnectionState
    {
        get => (NatsConnectionState)Volatile.Read(ref _connectionState);
        private set => Interlocked.Exchange(ref _connectionState, (int)value);
    }

    public INatsServerInfo? ServerInfo => WritableServerInfo; // server info is set when received INFO

    public NatsHeaderParser HeaderParser { get; }

    internal SubscriptionManager SubscriptionManager { get; }

    internal CommandWriter CommandWriter { get; }

    internal string InboxPrefix { get; }

    internal ObjectPool ObjectPool => _pool;

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public async ValueTask ConnectAsync()
    {
        if (ConnectionState == NatsConnectionState.Open)
            return;

        TaskCompletionSource? waiter = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (ConnectionState != NatsConnectionState.Closed)
            {
                waiter = _waitForOpenConnection;
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

        // Only Closed(initial) state, can run initial connect.
        await InitialConnectAsync().ConfigureAwait(false);
    }

    public NatsStats GetStats() => Counter.ToStats();

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _logger.Log(LogLevel.Information, NatsLogEvents.Connection, "Disposing connection {Name}", _name);

            await DisposeSocketAsync(false).ConfigureAwait(false);
            if (_pingTimerCancellationTokenSource != null)
            {
#if NET6_0
                _pingTimerCancellationTokenSource.Cancel();
#else
                await _pingTimerCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#endif
            }

            await SubscriptionManager.DisposeAsync().ConfigureAwait(false);
            await CommandWriter.DisposeAsync().ConfigureAwait(false);
            _waitForOpenConnection.TrySetCanceled();
#if NET6_0
            _disposedCancellationTokenSource.Cancel();
#else
            await _disposedCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#endif
        }
    }

    internal void EnqueuePing(PingCommand pingCommand)
    {
        // Enqueue Ping Command to current working reader.
        var reader = _socketReader;
        if (reader != null)
        {
            if (reader.TryEnqueuePing(pingCommand))
            {
                return;
            }
        }

        // Can not add PING, set fail.
        pingCommand.TaskCompletionSource.SetCanceled();
    }

    internal ValueTask PublishToClientHandlersAsync(string subject, string? replyTo, int sid, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer)
    {
        return SubscriptionManager.PublishToClientHandlersAsync(subject, replyTo, sid, headersBuffer, payloadBuffer);
    }

    internal void ResetPongCount()
    {
        Interlocked.Exchange(ref _pongCount, 0);
    }

    internal ValueTask PostPongAsync() => CommandWriter.PongAsync(CancellationToken.None);

    // called only internally
    internal ValueTask SubscribeCoreAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken) => CommandWriter.SubscribeAsync(sid, subject, queueGroup, maxMsgs, cancellationToken);

    internal ValueTask UnsubscribeAsync(int sid)
    {
        try
        {
            return CommandWriter.UnsubscribeAsync(sid, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // connection is disposed, don't need to unsubscribe command.
            if (_isDisposed)
            {
                return ValueTask.CompletedTask;
            }

            _logger.LogError(NatsLogEvents.Subscription, ex, "Failed to send unsubscribe command");
        }

        return ValueTask.CompletedTask;
    }

    internal void MessageDropped<T>(NatsSub<T> natsSub, int pending, NatsMsg<T> msg)
    {
        var subject = msg.Subject;
        _logger.LogWarning("Dropped message from {Subject} with {Pending} pending messages", subject, pending);
        OnError?.Invoke(this, new MessageDroppedError(natsSub, pending, subject, msg.ReplyTo, msg.Headers, msg.Data));
    }

    internal BoundedChannelOptions GetChannelOpts(NatsOpts connectionOpts, NatsSubChannelOpts? subChannelOpts)
    {
        if (subChannelOpts is { } overrideOpts)
        {
            return new BoundedChannelOptions(overrideOpts.Capacity ??
                                             _defaultSubscriptionChannelOpts.Capacity)
            {
                AllowSynchronousContinuations =
                    _defaultSubscriptionChannelOpts.AllowSynchronousContinuations,
                FullMode =
                    overrideOpts.FullMode ?? _defaultSubscriptionChannelOpts.FullMode,
                SingleWriter = _defaultSubscriptionChannelOpts.SingleWriter,
                SingleReader = _defaultSubscriptionChannelOpts.SingleReader,
            };
        }
        else
        {
            return _defaultSubscriptionChannelOpts;
        }
    }

    private async ValueTask InitialConnectAsync()
    {
        Debug.Assert(ConnectionState == NatsConnectionState.Connecting, "Connection state");

        var uris = Opts.GetSeedUris();

        foreach (var uri in uris)
        {
            if (Opts.TlsOpts.EffectiveMode(uri) == TlsMode.Disable && uri.IsTls)
                throw new NatsException($"URI {uri} requires TLS but TlsMode is set to Disable");
        }

        if (Opts.TlsOpts.HasTlsCerts)
            _tlsCerts = await TlsCerts.FromNatsTlsOptsAsync(Opts.TlsOpts).ConfigureAwait(false);

        if (!Opts.AuthOpts.IsAnonymous)
        {
            _userCredentials = new UserCredentials(Opts.AuthOpts);
        }

        foreach (var uri in uris)
        {
            try
            {
                var target = (uri.Host, uri.Port);
                if (OnConnectingAsync != null)
                {
                    _logger.LogInformation(NatsLogEvents.Connection, "Try to invoke OnConnectingAsync before connect to NATS");
                    target = await OnConnectingAsync(target).ConfigureAwait(false);
                }

                _logger.LogInformation(NatsLogEvents.Connection, "Try to connect NATS {0}", uri);
                if (uri.IsWebSocket)
                {
                    var conn = new WebSocketConnection();
                    await conn.ConnectAsync(uri.Uri, Opts.ConnectTimeout).ConfigureAwait(false);
                    _socket = conn;
                }
                else
                {
                    var conn = new TcpConnection(_logger);
                    await conn.ConnectAsync(target.Host, target.Port, Opts.ConnectTimeout).ConfigureAwait(false);
                    _socket = conn;

                    if (Opts.TlsOpts.EffectiveMode(uri) == TlsMode.Implicit)
                    {
                        // upgrade TcpConnection to SslConnection
                        var sslConnection = conn.UpgradeToSslStreamConnection(Opts.TlsOpts, _tlsCerts);
                        await sslConnection.AuthenticateAsClientAsync(uri).ConfigureAwait(false);
                        _socket = sslConnection;
                    }
                }

                _currentConnectUri = uri;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(NatsLogEvents.Connection, ex, "Fail to connect NATS {Url}", uri);
            }
        }

        if (_socket == null)
        {
            var exception = new NatsException("can not connect uris: " + string.Join(",", uris.Select(x => x.ToString())));
            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Closed; // allow retry connect
                _waitForOpenConnection.TrySetException(exception); // throw for waiter
                _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            var uri = _currentConnectUri;
            _currentConnectUri = null;
            var exception = new NatsException("can not start to connect nats server: " + uri, ex);
            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Closed; // allow retry connect
                _waitForOpenConnection.TrySetException(exception); // throw for waiter
                _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            throw exception;
        }

        lock (_gate)
        {
            var url = _currentConnectUri;
            _logger.LogInformation(NatsLogEvents.Connection, "Connect succeed {Name}, NATS {Url}", _name, url);
            ConnectionState = NatsConnectionState.Open;
            _pingTimerCancellationTokenSource = new CancellationTokenSource();
            StartPingTimer(_pingTimerCancellationTokenSource.Token);
            _waitForOpenConnection.TrySetResult();
            _ = Task.Run(ReconnectLoop);
            ConnectionOpened?.Invoke(this, url?.ToString() ?? string.Empty);
        }
    }

    private async ValueTask SetupReaderWriterAsync(bool reconnect)
    {
        if (_currentConnectUri!.IsSeed)
            _lastSeedConnectUri = _currentConnectUri;

        // create the socket reader
        var waitForInfoSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _socketReader = new NatsReadProtocolProcessor(_socket!, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);

        try
        {
            // wait for INFO
            await waitForInfoSignal.Task.ConfigureAwait(false);

            // check to see if we should upgrade to TLS
            if (_socket is TcpConnection tcpConnection)
            {
                if (Opts.TlsOpts.EffectiveMode(_currentConnectUri) == TlsMode.Disable && WritableServerInfo!.TlsRequired)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} requires TLS but TlsMode is set to Disable");
                }

                if (Opts.TlsOpts.EffectiveMode(_currentConnectUri) == TlsMode.Require && !WritableServerInfo!.TlsRequired && !WritableServerInfo.TlsAvailable)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} does not support TLS but TlsMode is set to Require");
                }

                if (Opts.TlsOpts.TryTls(_currentConnectUri) && (WritableServerInfo!.TlsRequired || WritableServerInfo.TlsAvailable))
                {
                    // do TLS upgrade
                    var targetUri = FixTlsHost(_currentConnectUri);

                    _logger.LogDebug(NatsLogEvents.Security, "Perform TLS Upgrade to {Uri}", targetUri);

                    // cancel INFO parsed signal and dispose current socket reader
                    infoParsedSignal.SetCanceled();
                    await _socketReader!.DisposeAsync().ConfigureAwait(false);
                    _socketReader = null;

                    // upgrade TcpConnection to SslConnection
                    var sslConnection = tcpConnection.UpgradeToSslStreamConnection(Opts.TlsOpts, _tlsCerts);
                    await sslConnection.AuthenticateAsClientAsync(targetUri).ConfigureAwait(false);
                    _socket = sslConnection;

                    // create new socket reader
                    waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _socketReader = new NatsReadProtocolProcessor(_socket, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);
                }
            }

            // mark INFO as parsed
            infoParsedSignal.SetResult();

            // Authentication
            _userCredentials?.Authenticate(_clientOpts, WritableServerInfo);

            await using (var priorityCommandWriter = new PriorityCommandWriter(_socket!, Opts, Counter))
            {
                // add CONNECT and PING command to priority lane
                await priorityCommandWriter.CommandWriter.ConnectAsync(_clientOpts, CancellationToken.None).ConfigureAwait(false);
                await priorityCommandWriter.CommandWriter.PingAsync(CancellationToken.None).ConfigureAwait(false);

                Task? reconnectTask = null;
                if (reconnect)
                {
                    // Reestablish subscriptions and consumers
                    reconnectTask = SubscriptionManager.WriteReconnectCommandsAsync(priorityCommandWriter.CommandWriter).AsTask();
                }

                // receive COMMAND response (PONG or ERROR)
                await waitForPongOrErrorSignal.Task.ConfigureAwait(false);

                if (reconnectTask != null)
                {
                    // wait for reconnect commands to complete
                    await reconnectTask.ConfigureAwait(false);
                }
            }

            // create the socket writer
            _socketWriter = CommandWriter.CreateNatsPipeliningWriteProtocolProcessor(_socket!);

            lock (_gate)
            {
                _lastAuthError = string.Empty;
            }
        }
        catch (Exception e)
        {
            if (e is NatsServerException { IsAuthError: true } se)
            {
                _logger.LogWarning(NatsLogEvents.Security, "Authentication error: {Error}", se.Error);

                var error = se.Error;
                string last;
                lock (_gate)
                {
                    last = _lastAuthError;
                    _lastAuthError = error;
                }

                if (!Opts.IgnoreAuthErrorAbort && string.Equals(last, error))
                {
                    lock (_gate)
                    {
                        _stopRetries = true;
                    }

                    _logger.LogError(NatsLogEvents.Security, "Received same authentication error ({Error}) twice in a row. Stopping retires", se.Error);
                }
            }

            infoParsedSignal.TrySetCanceled();
            await DisposeSocketAsync(true).ConfigureAwait(false);
            throw;
        }
    }

    private async void ReconnectLoop()
    {
        try
        {
            // If dispose this client, WaitForClosed throws OperationCanceledException so stop reconnect-loop correctly.
            await _socket!.WaitForClosed.ConfigureAwait(false);

            _logger.LogTrace(NatsLogEvents.Connection, "Connection {Name} is closed. Will cleanup and reconnect", _name);
            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Reconnecting;
                _waitForOpenConnection.TrySetCanceled();
                _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pingTimerCancellationTokenSource?.Cancel();
            }

            // Invoke after state changed
            ConnectionDisconnected?.Invoke(this, _currentConnectUri?.ToString() ?? string.Empty);

            // Cleanup current socket
            await DisposeSocketAsync(true).ConfigureAwait(false);

            var defaultScheme = _currentConnectUri!.Uri.Scheme;
            var urls = (Opts.NoRandomize
                           ? WritableServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, false, defaultScheme)).Distinct().ToArray()
                           : WritableServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, false, defaultScheme)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray())
                       ?? Array.Empty<NatsUri>();
            if (urls.Length == 0)
                urls = Opts.GetSeedUris();

            // add last.
            urls = urls.Where(x => x != _currentConnectUri).Append(_currentConnectUri).ToArray();

            _currentConnectUri = null;
            var urlEnumerator = urls.AsEnumerable().GetEnumerator();
            NatsUri? url = null;
        CONNECT_AGAIN:
            try
            {
                if (urlEnumerator.MoveNext())
                {
                    url = urlEnumerator.Current;

                    if (OnConnectingAsync != null)
                    {
                        var target = (url.Host, url.Port);
                        _logger.LogInformation(NatsLogEvents.Connection, "Try to invoke OnConnectingAsync before connect to NATS");
                        var newTarget = await OnConnectingAsync(target).ConfigureAwait(false);

                        if (newTarget.Host != target.Host || newTarget.Port != target.Port)
                        {
                            url = url.CloneWith(newTarget.Host, newTarget.Port);
                        }
                    }

                    _logger.LogInformation(NatsLogEvents.Connection, "Tried to connect NATS {Url}", url);
                    if (url.IsWebSocket)
                    {
                        var conn = new WebSocketConnection();
                        await conn.ConnectAsync(url.Uri, Opts.ConnectTimeout).ConfigureAwait(false);
                        _socket = conn;
                    }
                    else
                    {
                        var conn = new TcpConnection(_logger);
                        await conn.ConnectAsync(url.Host, url.Port, Opts.ConnectTimeout).ConfigureAwait(false);
                        _socket = conn;

                        if (Opts.TlsOpts.EffectiveMode(url) == TlsMode.Implicit)
                        {
                            // upgrade TcpConnection to SslConnection
                            var sslConnection = conn.UpgradeToSslStreamConnection(Opts.TlsOpts, _tlsCerts);
                            await sslConnection.AuthenticateAsClientAsync(FixTlsHost(url)).ConfigureAwait(false);
                            _socket = sslConnection;
                        }
                    }

                    _currentConnectUri = url;
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
                    _logger.LogError(NatsLogEvents.Connection, ex, "Failed to connect NATS {Url}", url);
                }

                ReconnectFailed?.Invoke(this, url?.ToString() ?? string.Empty);
                await WaitWithJitterAsync().ConfigureAwait(false);
                goto CONNECT_AGAIN;
            }

            lock (_gate)
            {
                _connectRetry = 0;
                _backoff = TimeSpan.Zero;
                _logger.LogInformation(NatsLogEvents.Connection, "Connection succeeded {Name}, NATS {Url}", _name, url);
                ConnectionState = NatsConnectionState.Open;
                _pingTimerCancellationTokenSource = new CancellationTokenSource();
                StartPingTimer(_pingTimerCancellationTokenSource.Token);
                _waitForOpenConnection.TrySetResult();
                _ = Task.Run(ReconnectLoop);
                ConnectionOpened?.Invoke(this, url?.ToString() ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                return;
            _waitForOpenConnection.TrySetException(ex);
            _logger.LogError(NatsLogEvents.Connection, ex, "Retry loop stopped and connection state is invalid");
        }
    }

    private NatsUri FixTlsHost(NatsUri uri)
    {
        var lastSeedConnectUri = _lastSeedConnectUri;
        var lastSeedHost = lastSeedConnectUri?.Host;

        if (string.IsNullOrEmpty(lastSeedHost))
            return uri;

        // if the current URI is not a seed URI and is not a DNS hostname, check the server cert against the
        // last seed hostname if it was a DNS hostname
        if (!uri.IsSeed
            && Uri.CheckHostName(uri.Host) != UriHostNameType.Dns
            && Uri.CheckHostName(lastSeedHost) == UriHostNameType.Dns)
        {
            return uri.CloneWith(lastSeedHost);
        }

        return uri;
    }

    private async Task WaitWithJitterAsync()
    {
        bool stop;
        int retry;
        TimeSpan backoff;
        lock (_gate)
        {
            stop = _stopRetries;
            retry = _connectRetry++;

            if (Opts.ReconnectWaitMin >= Opts.ReconnectWaitMax)
            {
                _backoff = Opts.ReconnectWaitMin;
            }
            else if (_backoff == TimeSpan.Zero)
            {
                _backoff = Opts.ReconnectWaitMin;
            }
            else if (_backoff == Opts.ReconnectWaitMax)
            {
            }
            else
            {
                _backoff *= 2;
                if (_backoff > Opts.ReconnectWaitMax)
                {
                    _backoff = Opts.ReconnectWaitMax;
                }
                else if (_backoff <= TimeSpan.Zero)
                {
                    _backoff = TimeSpan.FromSeconds(1);
                }
            }

            backoff = _backoff;
        }

        // After two auth errors we will not retry.
        if (stop)
            throw new NatsException("Won't retry anymore.");

        if (Opts.MaxReconnectRetry > 0 && retry > Opts.MaxReconnectRetry)
            throw new NatsException("Max connect retry exceeded.");

        var jitter = Random.Shared.NextDouble() * Opts.ReconnectJitter.TotalMilliseconds;
        var waitTime = TimeSpan.FromMilliseconds(jitter) + backoff;
        if (waitTime != TimeSpan.Zero)
        {
            _logger.LogTrace(NatsLogEvents.Connection, "Waiting {WaitMs}ms to reconnect", waitTime.TotalMilliseconds);
            await Task.Delay(waitTime).ConfigureAwait(false);
        }
    }

    private async void StartPingTimer(CancellationToken cancellationToken)
    {
        if (Opts.PingInterval == TimeSpan.Zero)
            return;

        var periodicTimer = new PeriodicTimer(Opts.PingInterval);
        ResetPongCount();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref _pongCount) > Opts.MaxPingOut)
                {
                    _logger.LogInformation(NatsLogEvents.Connection, "Server didn't respond to our ping requests. Aborting connection");
                    if (_socket != null)
                    {
                        await _socket.AbortConnectionAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                await PingOnlyAsync(cancellationToken).ConfigureAwait(false);
                await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private CancellationTimer GetRequestCommandTimer(CancellationToken cancellationToken)
    {
        return _cancellationTimerPool.Start(Opts.RequestTimeout, cancellationToken);
    }

    // catch and log all exceptions, enforcing the socketComponentDisposeTimeout
    private async ValueTask DisposeSocketComponentAsync(IAsyncDisposable component, string description)
    {
        try
        {
            var dispose = component.DisposeAsync();
            if (!dispose.IsCompletedSuccessfully)
                await dispose.AsTask().WaitAsync(_socketComponentDisposeTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(NatsLogEvents.Connection, ex, $"Error occured when disposing {description}");
        }
    }

    // Dispose Writer(Drain prepared queues -> write to socket)
    // Close Socket
    // Dispose Reader(Drain read buffers but no reads more)
    private async ValueTask DisposeSocketAsync(bool asyncReaderDispose)
    {
        // writer's internal buffer/channel is not thread-safe, must wait until complete.
        if (_socketWriter != null)
        {
            await DisposeSocketComponentAsync(_socketWriter, "socket writer").ConfigureAwait(false);
            _socketWriter = null;
        }

        if (_socket != null)
        {
            await DisposeSocketComponentAsync(_socket, "socket").ConfigureAwait(false);
            _socket = null;
        }

        if (_socketReader != null)
        {
            if (asyncReaderDispose)
            {
                // reader is not share state, can dispose asynchronously.
                var reader = _socketReader;
                _ = Task.Run(() => DisposeSocketComponentAsync(reader, "socket reader asynchronously"));
            }
            else
            {
                await DisposeSocketComponentAsync(_socketReader, "socket reader").ConfigureAwait(false);
            }

            _socketReader = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(null);
    }

    private async void WithConnect<T1>(T1 item1, Action<NatsConnection, T1> core)
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

    private async void WithConnect<T1, T2>(T1 item1, T2 item2, Action<NatsConnection, T1, T2> core)
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

    private async ValueTask<TResult> WithConnectAsync<T1, TResult>(T1 item1, Func<NatsConnection, T1, ValueTask<TResult>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this, item1).ConfigureAwait(false);
    }
}
