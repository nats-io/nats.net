using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;
#if NETSTANDARD
using Random = NATS.Client.Core.Internal.NetStandardExtensions.Random;
#endif

namespace NATS.Client.Core;

public enum NatsConnectionState
{
    Closed,
    Open,
    Connecting,
    Reconnecting,
    Failed,
}

internal enum NatsEvent
{
    ConnectionOpened,
    ConnectionDisconnected,
    ReconnectFailed,
    MessageDropped,
    LameDuckModeActivated,
    ConnectionFailed,
    SlowConsumerDetected,
}

public partial class NatsConnection : INatsConnection
{
#pragma warning disable SA1401
    internal readonly ConnectionStatsCounter Counter; // allow to call from external sources
#pragma warning restore SA1401
    private readonly object _gate = new object();
    private readonly ILogger<NatsConnection> _logger;
    private readonly ObjectPool _pool;
    private readonly CancellationTokenSource _disposedCts;
    private readonly CancellationTokenSource _initialConnectCts;
    private readonly string _name;
    private readonly TimeSpan _socketComponentDisposeTimeout = TimeSpan.FromSeconds(5);
    private readonly BoundedChannelOptions _defaultSubscriptionChannelOpts;
    private readonly Channel<(NatsEvent, NatsEventArgs)> _eventChannel;
    private readonly ClientOpts _clientOpts;
    private readonly SubscriptionManager _subscriptionManager;
    private readonly ReplyTaskFactory _replyTaskFactory;

    private ServerInfo? _writableServerInfo;
    private int _pongCount;
    private int _connectionState;
    private int _isDisposed;
    private int _reconnectCount;

    // when reconnected, make new instance.
    private SocketConnectionWrapper? _socketConnection;
    private CancellationTokenSource? _pingTimerCancellationTokenSource;
    private volatile NatsUri? _currentConnectUri;
    private volatile NatsUri? _lastSeedConnectUri;
    private NatsReadProtocolProcessor? _socketReader;
    private TaskCompletionSource _waitForOpenConnection;
    private UserCredentials? _userCredentials;
    private int _connectRetry;
    private TimeSpan _backoff = TimeSpan.Zero;
    private string _lastAuthError = string.Empty;
    private bool _stopRetries;
    private Task? _publishEventsTask;
    private Task? _reconnectLoopTask;

    public NatsConnection()
        : this(NatsOpts.Default)
    {
    }

    public NatsConnection(NatsOpts opts)
    {
        _logger = opts.LoggerFactory.CreateLogger<NatsConnection>();
        Opts = opts.ReadUserInfoFromConnectionString();
        ConnectionState = NatsConnectionState.Closed;
        _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _disposedCts = new CancellationTokenSource();
        _initialConnectCts = new CancellationTokenSource();
        _pool = new ObjectPool(opts.ObjectPoolSize);
        _name = opts.Name;
        Counter = new ConnectionStatsCounter();
        CommandWriter = new CommandWriter("main", this, _pool, Opts, Counter, EnqueuePing);
        InboxPrefix = NewInbox(opts.InboxPrefix);
        _subscriptionManager = new SubscriptionManager(this, InboxPrefix);
        _replyTaskFactory = new ReplyTaskFactory(this);
        _clientOpts = ClientOpts.Create(Opts);
        HeaderParser = new NatsHeaderParser(opts.HeaderEncoding);
        _defaultSubscriptionChannelOpts = new BoundedChannelOptions(opts.SubPendingChannelCapacity)
        {
            FullMode = opts.SubPendingChannelFullMode,
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        };

        // push consumer events to a channel so handlers can be awaited (also prevents user code from blocking us)
        _eventChannel = Channel.CreateUnbounded<(NatsEvent, NatsEventArgs)>(new UnboundedChannelOptions { AllowSynchronousContinuations = false, SingleWriter = false, SingleReader = true, });
        _publishEventsTask = Task.Run(PublishEventsAsync, _disposedCts.Token);
    }

    // events
    public event AsyncEventHandler<NatsEventArgs>? ConnectionDisconnected;

    public event AsyncEventHandler<NatsEventArgs>? ConnectionOpened;

    public event AsyncEventHandler<NatsEventArgs>? ReconnectFailed;

    public event AsyncEventHandler<NatsMessageDroppedEventArgs>? MessageDropped;

    public event AsyncEventHandler<NatsSlowConsumerEventArgs>? SlowConsumerDetected;

    public event AsyncEventHandler<NatsLameDuckModeActivatedEventArgs>? LameDuckModeActivated;

    public INatsConnection Connection => this;

    public NatsOpts Opts { get; }

    public NatsConnectionState ConnectionState
    {
        get => (NatsConnectionState)Interlocked.CompareExchange(ref _connectionState, 0, 0);
        private set
        {
            _logger.LogDebug(NatsLogEvents.Connection, "Connection state is changing from {OldState} to {NewState}", ConnectionState, value);
            Interlocked.Exchange(ref _connectionState, (int)value);
        }
    }

    public INatsServerInfo? ServerInfo => WritableServerInfo; // server info is set when received INFO

    public INatsSubscriptionManager SubscriptionManager => _subscriptionManager;

    public NatsHeaderParser HeaderParser { get; }

    // Hooks
    public Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync { get; set; }

    public Func<INatsSocketConnection, ValueTask<INatsSocketConnection>>? OnSocketAvailableAsync { get; set; }

    internal ServerInfo? WritableServerInfo
    {
        get => Interlocked.CompareExchange(ref _writableServerInfo, null, null);
        set
        {
            if (value?.LameDuckMode == true)
            {
                _eventChannel.Writer.TryWrite((NatsEvent.LameDuckModeActivated, new NatsLameDuckModeActivatedEventArgs(_currentConnectUri!.Uri)));
            }

            Interlocked.Exchange(ref _writableServerInfo, value);
        }
    }

    internal bool IsDisposed
    {
        get => Interlocked.CompareExchange(ref _isDisposed, 0, 0) == 1;
        private set => Interlocked.Exchange(ref _isDisposed, value ? 1 : 0);
    }

    internal CommandWriter CommandWriter { get; }

    internal string InboxPrefix { get; }

    internal ObjectPool ObjectPool => _pool;

    // only used for internal testing
    internal SocketConnectionWrapper? TestSocketConnection => _socketConnection;

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public async ValueTask ConnectAsync()
    {
        while (true)
        {
            try
            {
                if (ConnectionState == NatsConnectionState.Failed)
                    throw new NatsConnectionFailedException("Connection is in failed state");

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

                if (Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
                {
                    await _subscriptionManager.InitializeInboxSubscriptionAsync(_disposedCts.Token).ConfigureAwait(false);
                }
            }
            catch (NatsException)
            {
                if (!Opts.RetryOnInitialConnect)
                {
                    throw;
                }

                try
                {
                    await WaitWithJitterAsync(_initialConnectCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    /// <inheritdoc />
    public void OnMessageDropped<T>(NatsSubBase natsSub, int pending, NatsMsg<T> msg)
    {
        var subject = msg.Subject;
        _eventChannel.Writer.TryWrite((NatsEvent.MessageDropped, new NatsMessageDroppedEventArgs(natsSub, pending, subject, msg.ReplyTo, msg.Headers, msg.Data)));

        if (natsSub.TryMarkSlowConsumer())
        {
            _eventChannel.Writer.TryWrite((NatsEvent.SlowConsumerDetected, new NatsSlowConsumerEventArgs(natsSub)));

            if (!Opts.SuppressSlowConsumerWarnings)
            {
                _logger.LogWarning(NatsLogEvents.Subscription, "Slow consumer detected on subscription {Subject}", natsSub.Subject);
            }
        }
    }

    /// <inheritdoc />
    public BoundedChannelOptions GetBoundedChannelOpts(NatsSubChannelOpts? subChannelOpts)
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

    public virtual async ValueTask DisposeAsync()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            _logger.Log(LogLevel.Information, NatsLogEvents.Connection, "Disposing connection {Name}", _name);

            await DisposeSocketAsync(false).ConfigureAwait(false);
            if (_pingTimerCancellationTokenSource != null)
            {
#if NET8_0_OR_GREATER
                await _pingTimerCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#else
                _pingTimerCancellationTokenSource.Cancel();
#endif
            }

            await _subscriptionManager.DisposeAsync().ConfigureAwait(false);
            await CommandWriter.DisposeAsync().ConfigureAwait(false);
            _waitForOpenConnection.TrySetCanceled();
#if NET8_0_OR_GREATER
            await _disposedCts.CancelAsync().ConfigureAwait(false);
            await _initialConnectCts.CancelAsync().ConfigureAwait(false);
#else
            _disposedCts.Cancel();
            _initialConnectCts.Cancel();
#endif
        }
    }

    internal string SpanDestinationName(string subject)
    {
        if (subject.StartsWith(Opts.InboxPrefix, StringComparison.Ordinal))
            return "inbox";

        // to avoid long span names and low cardinality, only take the first two tokens
        var tokens = subject.Split('.');
        return tokens.Length < 2 ? subject : $"{tokens[0]}.{tokens[1]}";
    }

    internal NatsStats GetStats() => Counter.ToStats();

    internal ValueTask PublishToClientHandlersAsync(string subject, string? replyTo, int sid, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer)
    {
        if (Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
        {
            // Direct mode, check if the subject is an inbox
            // and if so, check if the subject is a reply to a request
            // by checking if the subject length is less than two NUIDs + dots
            // e.g. _INBOX.Hu5HPpWesrJhvQq2NG3YJ6.Hu5HPpWesrJhvQq2NG3YLw
            //  vs. _INBOX.Hu5HPpWesrJhvQq2NG3YJ6.1234
            // otherwise, it's not a reply in direct mode.
            if (_subscriptionManager.InboxSid == sid && subject.Length < InboxPrefix.Length + 1 + 22 + 1 + 22)
            {
                var idString = subject.AsSpan().Slice(InboxPrefix.Length + 1)
#if NETSTANDARD2_0
                    .ToString()
#endif
                ;

                if (long.TryParse(idString, out var id))
                {
                    if (_replyTaskFactory.TrySetResult(id, replyTo, payloadBuffer, headersBuffer))
                    {
                        return default;
                    }

                    // if we can't set the result, either the task is already timed out or
                    // it's not a reply to a request.
                }

                // if we can't parse the id, it's not a reply.
            }
        }

        return _subscriptionManager.PublishToClientHandlersAsync(subject, replyTo, sid, headersBuffer, payloadBuffer);
    }

    internal void ResetPongCount()
    {
        Interlocked.Exchange(ref _pongCount, 0);
    }

    internal ValueTask PongAsync() => CommandWriter.PongAsync(CancellationToken.None);

    // called only internally
    internal ValueTask SubscribeCoreAsync(int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken) => CommandWriter.SubscribeAsync(sid, subject, queueGroup, maxMsgs, cancellationToken);

    internal ValueTask UnsubscribeAsync(int sid)
    {
        try
        {
            // TODO: use maxMsgs in INatsSub<T> to unsubscribe.
            return CommandWriter.UnsubscribeAsync(sid, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // connection is disposed, don't need to unsubscribe command.
            if (IsDisposed)
            {
                return default;
            }

            _logger.LogError(NatsLogEvents.Subscription, ex, "Failed to send unsubscribe command");
        }

        return default;
    }

    private async ValueTask InitialConnectAsync()
    {
        Debug.Assert(ConnectionState == NatsConnectionState.Connecting, "Connection state");

        var uris = Opts.GetSeedUris();

        foreach (var uri in uris)
        {
            if (Opts.TlsOpts.EffectiveMode(uri.Uri) == TlsMode.Disable && uri.IsTls)
                throw new NatsException($"URI {uri} requires TLS but TlsMode is set to Disable");
        }

        if (!Opts.AuthOpts.IsAnonymous)
        {
            _userCredentials = new UserCredentials(Opts.AuthOpts);
        }

        var attemptedUris = new List<NatsUri>();
        foreach (var uri in uris)
        {
            try
            {
                await ConnectSocketAsync(uri).ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                var attempted = _currentConnectUri ?? uri;
                attemptedUris.Add(attempted);
                _logger.LogError(NatsLogEvents.Connection, ex, "Fail to connect NATS {Url}", attempted);
            }
        }

        if (_socketConnection == null)
        {
            var exception = new NatsException("can not connect uris: " + string.Join(",", attemptedUris.Select(x => x.ToString())));
            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Closed; // allow retry connect

                // throw for the waiter
                _waitForOpenConnection.TrySetObservedException(exception);
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

                // throw for the waiter
                _waitForOpenConnection.TrySetObservedException(exception);
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
#pragma warning disable VSTHRD103
            _initialConnectCts.Cancel();
#pragma warning restore VSTHRD103
            _reconnectLoopTask = Task.Run(ReconnectLoop);
            _eventChannel.Writer.TryWrite((NatsEvent.ConnectionOpened, new NatsEventArgs(url?.ToString() ?? string.Empty)));
        }
    }

    private async Task ConnectSocketAsync(NatsUri uri)
    {
        _currentConnectUri = uri;

        var target = (uri.Host, uri.Port);
        if (OnConnectingAsync != null)
        {
            _logger.LogInformation(NatsLogEvents.Connection, "Invoke OnConnectingAsync before connecting to NATS {Uri}", uri);
            target = await OnConnectingAsync(target).ConfigureAwait(false);
            if (target.Host != uri.Host || target.Port != uri.Port)
            {
                uri = uri with { Uri = new UriBuilder(uri.Uri) { Host = target.Host, Port = target.Port, }.Uri };
                _currentConnectUri = uri;
            }
        }

        var connectionFactory = Opts.SocketConnectionFactory ?? (uri.IsWebSocket ? WebSocketFactory.Default : TcpFactory.Default);
        _logger.LogInformation(NatsLogEvents.Connection, "Connect to NATS using {FactoryType} {Uri}", connectionFactory.GetType().Name, uri);
        using var timeoutCts = new CancellationTokenSource(Opts.ConnectTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token, timeoutCts.Token);
        try
        {
            var socketConnection = await connectionFactory.ConnectAsync(uri.Uri, Opts, cts.Token).ConfigureAwait(false);
            _socketConnection = new SocketConnectionWrapper(socketConnection);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new SocketException(10060); // 10060 = connection timeout.
        }

        if (Opts.TlsOpts.EffectiveMode(uri.Uri) == TlsMode.Implicit
            && _socketConnection.InnerSocket is INatsTlsUpgradeableSocketConnection tlsUpgradeableSocketConnection)
        {
            _logger.LogDebug(NatsLogEvents.Security, "Perform implicit TLS Upgrade to {Uri}", uri);
            var sslConnection = new SslStreamConnection(tlsUpgradeableSocketConnection, Opts.TlsOpts);
            await sslConnection.AuthenticateAsClientAsync(uri, Opts.ConnectTimeout).ConfigureAwait(false);
            _socketConnection = _socketConnection with { InnerSocket = sslConnection };
        }

        if (OnSocketAvailableAsync != null)
        {
            _logger.LogInformation(NatsLogEvents.Connection, "Invoke OnSocketAvailable after connecting to NATS {Uri}", uri);
            _socketConnection = _socketConnection with { InnerSocket = await OnSocketAvailableAsync(_socketConnection.InnerSocket).ConfigureAwait(false) };
        }
    }

    private async ValueTask SetupReaderWriterAsync(bool reconnect)
    {
        _logger.LogDebug(NatsLogEvents.Connection, "Setup reader and writer");

        if (_currentConnectUri!.IsSeed)
            _lastSeedConnectUri = _currentConnectUri;

        // create the socket reader
        var waitForInfoSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _socketReader = new NatsReadProtocolProcessor(_socketConnection!, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);

        try
        {
            // Wait for an INFO message from server. If we land on a dead socket and server response
            // can't be received, this will throw a timeout exception and we will retry the connection.
            await waitForInfoSignal.Task.WaitAsync(Opts.ConnectTimeout).ConfigureAwait(false);

            // check to see if we should upgrade to TLS
            if (_socketConnection!.InnerSocket is INatsTlsUpgradeableSocketConnection tlsUpgradeableSocket)
            {
                if (Opts.TlsOpts.EffectiveMode(_currentConnectUri.Uri) == TlsMode.Disable && WritableServerInfo!.TlsRequired)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} requires TLS but TlsMode is set to Disable");
                }

                if (Opts.TlsOpts.EffectiveMode(_currentConnectUri.Uri) == TlsMode.Require && !WritableServerInfo!.TlsRequired && !WritableServerInfo.TlsAvailable)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} does not support TLS but TlsMode is set to Require");
                }

                if (Opts.TlsOpts.TryTls(_currentConnectUri.Uri) && (WritableServerInfo!.TlsRequired || WritableServerInfo.TlsAvailable))
                {
                    // do TLS upgrade
                    var targetUri = FixTlsHost(_currentConnectUri);

                    _logger.LogDebug(NatsLogEvents.Security, "Perform TLS Upgrade to {Uri}", targetUri);

                    // cancel INFO parsed signal and dispose current socket reader
                    infoParsedSignal.SetCanceled();
                    await _socketReader!.DisposeAsync().ConfigureAwait(false);
                    _socketReader = null;

                    // upgrade TcpConnection to SslConnection
                    var sslConnection = new SslStreamConnection(tlsUpgradeableSocket, Opts.TlsOpts);
                    await sslConnection.AuthenticateAsClientAsync(targetUri, Opts.ConnectTimeout).ConfigureAwait(false);
                    _socketConnection = _socketConnection with { InnerSocket = sslConnection };

                    // create new socket reader
                    waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _socketReader = new NatsReadProtocolProcessor(_socketConnection, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);
                }
            }

            // mark INFO as parsed
            infoParsedSignal.SetResult();

            // Authentication
            if (_userCredentials != null)
            {
                await _userCredentials.AuthenticateAsync(_clientOpts, WritableServerInfo, _currentConnectUri, Opts.ConnectTimeout, _disposedCts.Token).ConfigureAwait(false);
            }

            await using (var priorityCommandWriter = new PriorityCommandWriter(this, _pool, _socketConnection!, Opts, Counter, EnqueuePing))
            {
                // add CONNECT and PING command to priority lane
                await priorityCommandWriter.CommandWriter.ConnectAsync(_clientOpts, CancellationToken.None).ConfigureAwait(false);
                await priorityCommandWriter.CommandWriter.PingAsync(new PingCommand(_pool), CancellationToken.None).ConfigureAwait(false);

                Task? reconnectTask = null;
                if (reconnect)
                {
                    // Reestablish subscriptions and consumers
                    reconnectTask = _subscriptionManager.WriteReconnectCommandsAsync(priorityCommandWriter.CommandWriter).AsTask();
                }

                // receive COMMAND response (PONG or ERROR)
                try
                {
                    await waitForPongOrErrorSignal.Task
                        .WaitAsync(Opts.ConnectTimeout)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogDebug(NatsLogEvents.Connection, "Timeout waiting for initial pong");
                    throw;
                }

                if (reconnectTask != null)
                {
                    // wait for reconnect commands to complete
                    await reconnectTask.ConfigureAwait(false);
                }
            }

            // create the socket writer
            CommandWriter.Reset(_socketConnection!);

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

        _logger.LogDebug(NatsLogEvents.Connection, "Setup reader and writer completed successfully");
    }

    private async void ReconnectLoop()
    {
        var reconnectCount = Interlocked.Increment(ref _reconnectCount);
        _logger.LogDebug(NatsLogEvents.Connection, "Reconnect loop started [{ReconnectCount}]", reconnectCount);
        var debug = _logger.IsEnabled(LogLevel.Debug);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            try
            {
                // wait for the current socket to complete or throw
                await _socketConnection!.WaitForClosed.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // If the NatsConnection is disposed, WaitForClosed throws so stop reconnect-loop correctly
                if (CheckDisposed())
                {
                    return;
                }

                // otherwise ignore the exception and reconnect
            }

            _logger.LogDebug(NatsLogEvents.Connection, "Reconnect loop connection closed [{ReconnectCount}]", reconnectCount);

            await CommandWriter.CancelReaderLoopAsync().ConfigureAwait(false);

            _logger.LogDebug(NatsLogEvents.Connection, "Connection {Name} is closed. Will cleanup and reconnect [{ReconnectCount}]", _name, reconnectCount);

            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Reconnecting;
                _waitForOpenConnection.TrySetCanceled();
                _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pingTimerCancellationTokenSource?.Cancel();
            }

            // Invoke event after state changed
            _eventChannel.Writer.TryWrite((NatsEvent.ConnectionDisconnected, new NatsEventArgs(_currentConnectUri?.ToString() ?? string.Empty)));

            // Cleanup current socket
            await DisposeSocketAsync(true).ConfigureAwait(false);

            var defaultScheme = _currentConnectUri!.Uri.Scheme;
            var serverReportedUrls = ServerInfo?
                                         .ClientConnectUrls?
                                         .Select(x => new NatsUri(x, false, defaultScheme))
                                     ?? Array.Empty<NatsUri>();

            // Always keep the original seed URLs in the list of URLs to connect to
            var urls = serverReportedUrls.Concat(Opts.GetSeedUris()).Distinct();
            if (!Opts.NoRandomize)
            {
                urls = urls.OrderBy(_ => Guid.NewGuid());
            }

            // Ensure the current URL is last in the list
            urls = urls.Where(x => x != _currentConnectUri).Append(_currentConnectUri).ToArray();

            _currentConnectUri = null;
            var urlEnumerator = urls.AsEnumerable().GetEnumerator();
            NatsUri? url = null;
        CONNECT_AGAIN:

            _logger.LogDebug(NatsLogEvents.Connection, "Trying to reconnect [{ReconnectCount}]", reconnectCount);

            if (CheckDisposed())
            {
                // No point in trying to reconnect.
                // This can happen if we're disposed while we're waiting for the next reconnect
                // and potentially gets us stuck in a reconnect loop.
                return;
            }

            try
            {
                if (urlEnumerator.MoveNext())
                {
                    url = urlEnumerator.Current;
                    _logger.LogInformation(NatsLogEvents.Connection, "Reconnecting to NATS {Url} [{ReconnectCount}]", url, reconnectCount);
                    await ConnectSocketAsync(url).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug(NatsLogEvents.Connection, "Reconnect URLs exhausted, retrying from the beginning [{ReconnectCount}]", reconnectCount);
                    urlEnumerator.Dispose();
                    urlEnumerator = urls.AsEnumerable().GetEnumerator();
                    goto CONNECT_AGAIN;
                }

                await SetupReaderWriterAsync(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (CheckDisposed())
                {
                    // No point in trying to reconnect.
                    return;
                }

                var attempted = _currentConnectUri ?? url;
                _logger.LogWarning(NatsLogEvents.Connection, ex, "Failed to connect NATS {Url} [{ReconnectCount}]", attempted, reconnectCount);

                _eventChannel.Writer.TryWrite((NatsEvent.ReconnectFailed, new NatsEventArgs(attempted?.ToString() ?? string.Empty)));

                if (debug)
                {
                    stopwatch.Restart();
                    _logger.LogDebug(NatsLogEvents.Connection, "Reconnect wait with jitter [{ReconnectCount}]", reconnectCount);
                }

                await WaitWithJitterAsync(_disposedCts.Token).ConfigureAwait(false);

                if (debug)
                {
                    stopwatch.Stop();
                    _logger.LogDebug(NatsLogEvents.Connection, "Reconnect wait over after {WaitMs}ms [{ReconnectCount}]", stopwatch.ElapsedMilliseconds, reconnectCount);
                }

                goto CONNECT_AGAIN;
            }

            lock (_gate)
            {
                _connectRetry = 0;
                _backoff = TimeSpan.Zero;
                _logger.LogInformation(NatsLogEvents.Connection, "Connection succeeded {Name}, NATS {Url} [{ReconnectCount}]", _name, url, reconnectCount);
                ConnectionState = NatsConnectionState.Open;
                _pingTimerCancellationTokenSource = new CancellationTokenSource();
                StartPingTimer(_pingTimerCancellationTokenSource.Token);
                _waitForOpenConnection.TrySetResult();
                _reconnectLoopTask = Task.Run(ReconnectLoop);
                _eventChannel.Writer.TryWrite((NatsEvent.ConnectionOpened, new NatsEventArgs(url.ToString())));
            }
        }
        catch (Exception ex)
        {
            _waitForOpenConnection.TrySetObservedException(ex);
            try
            {
                if (!IsDisposed)
                {
                    // Only log if we're not disposing, otherwise we might log exceptions that are expected
                    _logger.LogError(NatsLogEvents.Connection, ex, "Retry loop stopped and connection state is invalid [{ReconnectCount}]", reconnectCount);
                }
            }
            catch
            {
                // ignore logging exceptions since our host might be disposed or shutting down
                // and some logging providers might throw exceptions when they can't log
                // which in turn would crash the application.
                // (e.g. we've seen this with EventLog provider on Windows)
            }
        }

        try
        {
            _logger.LogDebug(NatsLogEvents.Connection, "Reconnect loop stopped [{ReconnectCount}]", reconnectCount);
        }
        catch
        {
            // ignore logging exceptions since our host might be disposed or shutting down
        }

        return;

        bool CheckDisposed()
        {
            if (IsDisposed)
            {
                try
                {
                    _logger.LogDebug(NatsLogEvents.Connection, "Retry loop stopped because the connection was disposed [{ReconnectCount}]", reconnectCount);
                }
                catch
                {
                    // ignore logging exceptions since our host might be disposed or shutting down
                }

                return true;
            }

            return false;
        }
    }

    private async Task PublishEventsAsync()
    {
        try
        {
            while (!_disposedCts.IsCancellationRequested)
            {
                var hasData = await _eventChannel.Reader.WaitToReadAsync(_disposedCts.Token).ConfigureAwait(false);
                while (hasData && _eventChannel.Reader.TryRead(out var eventArgs))
                {
                    var (natsEvent, args) = eventArgs;
                    switch (natsEvent)
                    {
                    case NatsEvent.ConnectionOpened when ConnectionOpened != null:
                        await ConnectionOpened.InvokeAsync(this, args).ConfigureAwait(false);
                        break;
                    case NatsEvent.ConnectionDisconnected when ConnectionDisconnected != null:
                        await ConnectionDisconnected.InvokeAsync(this, args).ConfigureAwait(false);
                        break;
                    case NatsEvent.ReconnectFailed when ReconnectFailed != null:
                        await ReconnectFailed.InvokeAsync(this, args).ConfigureAwait(false);
                        break;
                    case NatsEvent.MessageDropped when MessageDropped != null && args is NatsMessageDroppedEventArgs error:
                        await MessageDropped.InvokeAsync(this, error).ConfigureAwait(false);
                        break;
                    case NatsEvent.SlowConsumerDetected when SlowConsumerDetected != null && args is NatsSlowConsumerEventArgs slowConsumer:
                        await SlowConsumerDetected.InvokeAsync(this, slowConsumer).ConfigureAwait(false);
                        break;
                    case NatsEvent.LameDuckModeActivated when LameDuckModeActivated != null && args is NatsLameDuckModeActivatedEventArgs uri:
                        await LameDuckModeActivated.InvokeAsync(this, uri).ConfigureAwait(false);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_disposedCts.IsCancellationRequested)
        {
            // Ignore, we're disposing the connection
        }
        catch (Exception ex)
        {
            _logger.LogError(NatsLogEvents.Connection, ex, "Error occured when publishing events");
            if (!_disposedCts.IsCancellationRequested)
                _publishEventsTask = Task.Run(PublishEventsAsync, _disposedCts.Token);
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
            return uri with
            {
                Uri = new UriBuilder(uri.Uri)
                {
#if NETSTANDARD2_0
                    Host = lastSeedHost!,
#else
                    Host = lastSeedHost,
#endif
                }.Uri,
            };
        }

        return uri;
    }

    private async Task WaitWithJitterAsync(CancellationToken cancellationToken)
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
                _backoff = new TimeSpan(_backoff.Ticks * 2);
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

            // After two auth errors we will not retry.
            if (stop)
            {
                ConnectionState = NatsConnectionState.Failed;
                throw new NatsConnectionFailedException("Maximum authentication attempts exceeded");
            }

            if (Opts.MaxReconnectRetry > 0 && retry > Opts.MaxReconnectRetry)
            {
                ConnectionState = NatsConnectionState.Failed;
                throw new NatsConnectionFailedException("Maximum connection retry attempts exceeded");
            }
        }

        var jitter = Random.Shared.NextDouble() * Opts.ReconnectJitter.TotalMilliseconds;
        var waitTime = TimeSpan.FromMilliseconds(jitter) + backoff;
        if (waitTime != TimeSpan.Zero)
        {
            _logger.LogTrace(NatsLogEvents.Connection, "Waiting {WaitMs}ms to reconnect", waitTime.TotalMilliseconds);
            await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
        }
    }

    private async void StartPingTimer(CancellationToken cancellationToken)
    {
        if (Opts.PingInterval == TimeSpan.Zero)
            return;

        _logger.LogDebug(NatsLogEvents.Connection, "Starting ping timer");

        using var periodicTimer = new PeriodicTimer(Opts.PingInterval);
        ResetPongCount();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref _pongCount) > Opts.MaxPingOut)
                {
                    _logger.LogInformation(NatsLogEvents.Connection, "Server didn't respond to our ping requests. Aborting connection");
                    if (_socketConnection != null)
                    {
                        await _socketConnection.DisposeAsync().ConfigureAwait(false);
                        return;
                    }
                }

                await PingOnlyAsync(cancellationToken).ConfigureAwait(false);
                await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelling is part of a normal reconnect operation, so we don't need to log this
        }
        catch (Exception e)
        {
            if (!IsDisposed)
            {
                try
                {
                    _logger.LogWarning(NatsLogEvents.Connection, e, "Ping timer error");
                }
                catch
                {
                    // ignore logging exceptions in case our host might be disposed or shutting down
                }
            }
        }

        try
        {
            _logger.LogDebug(NatsLogEvents.Connection, "Ping timer stopped");
        }
        catch
        {
            // ignore logging exceptions in case our host might be disposed or shutting down
        }
    }

    private void EnqueuePing(PingCommand pingCommand)
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
        pingCommand.SetCanceled();
    }

    // catch and log all exceptions, enforcing the socketComponentDisposeTimeout
    private async ValueTask DisposeSocketComponentAsync(IAsyncDisposable component, string description)
    {
        try
        {
            _logger.LogDebug(NatsLogEvents.Connection, "Dispose socket component {Description}", description);
        }
        catch
        {
            // ignore logging exceptions in case our host might be disposed or shutting down
        }

        try
        {
            var dispose = component.DisposeAsync();
            if (!dispose.IsCompletedSuccessfully)
                await dispose.AsTask().WaitAsync(_socketComponentDisposeTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogError(NatsLogEvents.Connection, ex, $"Error occured when disposing {description}");
            }
            catch
            {
                // ignore logging exceptions in case our host might be disposed or shutting down
            }
        }
    }

    // Dispose Writer(Drain prepared queues -> write to socket)
    // Close Socket
    // Dispose Reader(Drain read buffers but no reads more)
    private async ValueTask DisposeSocketAsync(bool asyncReaderDispose)
    {
        try
        {
            _logger.LogDebug(NatsLogEvents.Connection, "Disposing socket");
        }
        catch
        {
            // ignore logging exceptions in case our host might be disposed or shutting down
        }

        if (_socketConnection != null)
        {
            await DisposeSocketComponentAsync(_socketConnection, "socket").ConfigureAwait(false);
            _socketConnection = null;
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
        if (IsDisposed)
            throw new ObjectDisposedException(null);
    }
}
