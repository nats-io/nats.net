using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
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
    ServerError,
}

public partial class NatsConnection : INatsConnection
{
#pragma warning disable SA1401
    internal readonly ConnectionStatsCounter Counter; // allow to call from external sources
#pragma warning restore SA1401
#if NET9_0_OR_GREATER
    private readonly System.Threading.Lock _gate = new();
#else
    private readonly object _gate = new object();
#endif
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
    private readonly object _drainParticipantsGate = new();
    private readonly HashSet<NatsSubBase> _drainParticipants = new();
    private readonly object _connectUrlsGate = new();

    private ServerInfo? _writableServerInfo;

    // Servers learned from INFO connect_urls, each with the scheme of the connection it was
    // learned on. The connect_urls of a plaintext INFO received before a TLS upgrade are not used.
    private (string Entry, string Scheme)[]? _discoveredServers;
    private bool _currentConnectionTls;
    private bool? _tlsUrlConfigured;
    private KeyValuePair<string, object?>[]? _metricTagsPrefix;
    private object? _boxedServerPort;
    private string? _serverHost;
    private string? _clientId;
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
        HeaderParser = new NatsHeaderParser(opts.HeaderEncoding, opts.CaseSensitiveHeaders);
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

    public event AsyncEventHandler<NatsServerErrorEventArgs>? ServerError;

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
                PushEvent(NatsEvent.LameDuckModeActivated, new NatsLameDuckModeActivatedEventArgs(_currentConnectUri!.Uri));
            }

            KeyValuePair<string, object?>[]? prefix = null;
            object? port = null;
            string? host = null;
            string? clientId = null;
            if (value is not null)
            {
                // ServerInfo.Host/Port is the server's bind address (often 0.0.0.0); use the
                // address the client actually dialled for OTel server.address/server.port.
                var connectUri = _currentConnectUri;
                host = connectUri?.Host ?? value.Host;
                port = connectUri?.Port ?? value.Port;
                clientId = value.ClientId.ToString();
                prefix = new[]
                {
                    new KeyValuePair<string, object?>(Telemetry.Constants.SystemKey, Telemetry.Constants.SystemVal),
                    new KeyValuePair<string, object?>(Telemetry.Constants.ServerAddress, host),
                    new KeyValuePair<string, object?>(Telemetry.Constants.ServerPort, port),
                    new KeyValuePair<string, object?>(Telemetry.Constants.NetworkProtoName, "nats"),
                    new KeyValuePair<string, object?>(Telemetry.Constants.NetworkTransport, "tcp"),
                };
            }

            // Cache the trace-tag host, boxed port and client id once per ServerInfo change so the
            // per-message trace path can reuse them instead of re-boxing the port and re-allocating
            // the client id string on every message. Host and port come from the connect URI, so the
            // trace and metric tags now share the same source.
            Volatile.Write(ref _boxedServerPort, port);
            Volatile.Write(ref _serverHost, host);
            Volatile.Write(ref _clientId, clientId);

            // Publish the cached fields before ServerInfo so any reader observing the new ServerInfo
            // is guaranteed to also observe the matching tags.
            Volatile.Write(ref _metricTagsPrefix, prefix);
            Interlocked.Exchange(ref _writableServerInfo, value);
        }
    }

    internal KeyValuePair<string, object?>[]? MetricTagsPrefix => Volatile.Read(ref _metricTagsPrefix);

    internal object? BoxedServerPort => Volatile.Read(ref _boxedServerPort);

    internal string? ServerHost => Volatile.Read(ref _serverHost);

    internal string? ClientId => Volatile.Read(ref _clientId);

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
        // A configuration that can never connect fails here, outside the retry loop:
        // RetryOnInitialConnect would otherwise retry it forever without a word.
        ThrowIfTlsMisconfigured();

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
        if (Telemetry.DroppedMessages.Enabled)
            Telemetry.DroppedMessages.Add(1, Telemetry.BuildMetricTags(this, Telemetry.Constants.OpRec));

        // A dropped message never reaches a consumer, and it is the read from the subscription
        // channel that ends a receive activity. This is the only place the dropped message is
        // still in hand, so it is the last chance to end its activity.
        Telemetry.EndActivity(msg.Headers?.Activity);

        var subject = msg.Subject;
        PushEvent(NatsEvent.MessageDropped, new NatsMessageDroppedEventArgs(natsSub, pending, subject, msg.ReplyTo, msg.Headers, msg.Data));

        if (natsSub.TryMarkSlowConsumer())
        {
            PushEvent(NatsEvent.SlowConsumerDetected, new NatsSlowConsumerEventArgs(natsSub));

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

            if (_pingTimerCancellationTokenSource != null)
            {
#if NET8_0_OR_GREATER
                await _pingTimerCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#else
                _pingTimerCancellationTokenSource.Cancel();
#endif
            }

            if (Opts.DrainSubscriptionsOnDispose)
            {
                // Drain subs and flush the writer first so UNSUB/PING/PONG
                // and any pending acks can land before the socket closes.
                try
                {
                    await _subscriptionManager.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await DrainRemainingParticipantsAsync().ConfigureAwait(false);
                    await CommandWriter.DisposeAsync().ConfigureAwait(false);
                    await DisposeSocketAsync(false).ConfigureAwait(false);
                }
            }
            else
            {
                await DisposeSocketAsync(false).ConfigureAwait(false);
                await _subscriptionManager.DisposeAsync().ConfigureAwait(false);
                await CommandWriter.DisposeAsync().ConfigureAwait(false);
            }

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

        if (NatsInstrumentationOptions.Default.SpanDestinationNameFormatter is { } formatter)
        {
            try
            {
                return formatter(subject);
            }
            catch
            {
                // A formatter is only choosing a span name. Letting it throw would take out the
                // publish, or the message build on receive, which is never a trade worth making
                // for a name. Fall through to the default below.
            }
        }

        // to avoid long span names and low cardinality, only take the first two tokens
        var subjectSpan = subject.AsSpan();
        var firstSeparator = subjectSpan.IndexOf('.');
        var secondSeparator = firstSeparator < 0 ? -1 : subjectSpan[(firstSeparator + 1)..].IndexOf('.');
        return secondSeparator < 0
            ? subject
            : subjectSpan[..(firstSeparator + 1 + secondSeparator)].ToString();
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

    internal void RegisterDrainParticipant(NatsSubBase sub)
    {
        lock (_drainParticipantsGate)
            _drainParticipants.Add(sub);
    }

    internal void UnregisterDrainParticipant(NatsSubBase sub)
    {
        lock (_drainParticipantsGate)
            _drainParticipants.Remove(sub);
    }

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

    internal void PushEvent(NatsEvent @event, NatsEventArgs args)
        => _eventChannel.Writer.TryWrite((@event, args));

    // An INFO after the first one of its connection: it came over the connection's final
    // transport (TLS if there is TLS) and is what the server itself says.
    internal void OnAsyncInfo(ServerInfo info) => SetDiscoveredServers(info.ClientConnectUrls);

    private void ThrowIfTlsMisconfigured()
    {
        foreach (var uri in Opts.GetSeedUris(suppressRandomization: true))
        {
            var tlsMode = EffectiveTlsMode(uri.Uri);
            if (tlsMode == TlsMode.Disable && NatsTlsOpts.IsTlsScheme(uri.Uri))
                throw new NatsException($"URI {uri} requires TLS but TlsMode is set to Disable");

            // TLS can't be added to a ws:// connection later, the scheme decides it
            if (tlsMode is TlsMode.Require or TlsMode.Implicit && Opts.SocketConnectionFactory == null && uri.Uri.Scheme == "ws")
                throw new NatsException($"TLS is required ({TlsRequiredReason(uri.Uri)}) but {uri} is a plaintext websocket URL: use wss://");
        }
    }

    private async ValueTask InitialConnectAsync()
    {
        Debug.Assert(ConnectionState == NatsConnectionState.Connecting, "Connection state");

        var uris = Opts.GetSeedUris();

        try
        {
            if (!Opts.AuthOpts.IsAnonymous)
            {
                _userCredentials = new UserCredentials(Opts.AuthOpts);
            }
        }
        catch (Exception ex)
        {
            var exception = ex as NatsException ?? new NatsException($"Failed to load credentials: {ex.Message}", ex);
            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Closed; // allow retry connect

                // throw for the waiter
                _waitForOpenConnection.TrySetObservedException(exception);
                _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            throw exception;
        }

        var attemptedUris = new List<NatsUri>();
        var connected = false;
        foreach (var uri in uris)
        {
            try
            {
                await ConnectSocketAsync(uri).ConfigureAwait(false);
                connected = true;
                break;
            }
            catch (Exception ex)
            {
                var attempted = _currentConnectUri ?? uri;
                attemptedUris.Add(attempted);
                _logger.LogError(NatsLogEvents.Connection, ex, "Fail to connect NATS {Url}", attempted);
            }
        }

        if (!connected)
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

        string openedUrl;
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
            openedUrl = url?.ToString() ?? string.Empty;
        }

        // Queue the event outside the connection lock: the lock protects the connection state only,
        // the event channel write must not happen while holding it.
        PushEvent(NatsEvent.ConnectionOpened, new NatsEventArgs(openedUrl));

        // The reconnect loop is started only after the open event is queued, so a disconnect can
        // never be reported before the matching open.
        _reconnectLoopTask = Task.Run(ReconnectLoop);
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

        var tlsMode = EffectiveTlsMode(uri.Uri);
        var tlsRequired = tlsMode is TlsMode.Require or TlsMode.Implicit;
        if (tlsRequired && Opts.SocketConnectionFactory == null && uri.Uri.Scheme == "ws")
            throw new NatsException($"TLS is required ({TlsRequiredReason(uri.Uri)}) but {uri} is a plaintext websocket URL: use wss://");

        var connectionFactory = Opts.SocketConnectionFactory ?? (uri.IsWebSocket ? WebSocketFactory.Default : TcpFactory.Default);
        _logger.LogInformation(NatsLogEvents.Connection, "Connect to NATS using {FactoryType} {Uri}", connectionFactory.GetType().Name, uri);
        using var timeoutCts = new CancellationTokenSource(Opts.ConnectTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token, timeoutCts.Token);
        INatsSocketConnection socketConnection;
        try
        {
            socketConnection = await connectionFactory.ConnectAsync(uri.Uri, Opts, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new SocketException(10060); // 10060 = connection timeout.
        }

        // The socket only becomes the connection's once the attempt has succeeded. If the
        // implicit TLS handshake fails, it must be closed here: carrying on with it would
        // talk plaintext to a server whose certificate was just rejected.
        try
        {
            // wss: ClientWebSocket did the TLS handshake
            var tls = connectionFactory is WebSocketFactory && uri.Uri.Scheme == "wss";

            if (tlsMode == TlsMode.Implicit
                && socketConnection is INatsTlsUpgradeableSocketConnection tlsUpgradeableSocketConnection and not INatsTlsSocketConnection)
            {
                _logger.LogDebug(NatsLogEvents.Security, "Perform implicit TLS Upgrade to {Uri}", uri);
                var sslConnection = new SslStreamConnection(tlsUpgradeableSocketConnection, Opts.TlsOpts);
                socketConnection = sslConnection;

                // the connection doesn't own this socket yet: close it here when disposed
                var socket = tlsUpgradeableSocketConnection.Socket;
                using (_disposedCts.Token.Register(() => socket.Dispose()))
                {
                    await sslConnection.AuthenticateAsClientAsync(uri, Opts.ConnectTimeout).ConfigureAwait(false);
                }

                tls = true;
            }

            if (OnSocketAvailableAsync != null)
            {
                _logger.LogInformation(NatsLogEvents.Connection, "Invoke OnSocketAvailable after connecting to NATS {Uri}", uri);
                var replacement = await OnSocketAvailableAsync(socketConnection).ConfigureAwait(false);

                // a different socket is not the one the TLS handshake was done on
                if (!ReferenceEquals(replacement, socketConnection))
                    tls = false;
                socketConnection = replacement;
            }

            // A transport the client can't upgrade (a custom factory, a wrapper from the hook)
            // would otherwise carry the connection, credentials included, in plaintext.
            if ((tlsRequired || NatsTlsOpts.IsTlsScheme(uri.Uri))
                && !tls
                && socketConnection is not INatsTlsSocketConnection
                && !(tlsMode == TlsMode.Require && socketConnection is INatsTlsUpgradeableSocketConnection))
            {
                throw new NatsException($"TLS is required for {uri} ({TlsRequiredReason(uri.Uri)}), but the socket connection ({socketConnection.GetType().Name}) can't be upgraded to TLS. "
                                        + "A socket connection from SocketConnectionFactory or OnSocketAvailableAsync must implement INatsTlsUpgradeableSocketConnection, "
                                        + "or INatsTlsSocketConnection if it provides TLS itself.");
            }

            _currentConnectionTls = tls || socketConnection is INatsTlsSocketConnection;
        }
        catch
        {
            await DisposeSocketComponentAsync(socketConnection, "socket of failed connection attempt").ConfigureAwait(false);
            throw;
        }

        _socketConnection = new SocketConnectionWrapper(socketConnection);
    }

    private async ValueTask SetupReaderWriterAsync(bool reconnect)
    {
        _logger.LogDebug(NatsLogEvents.Connection, "Setup reader and writer");

        if (_currentConnectUri!.IsSeed)
            _lastSeedConnectUri = _currentConnectUri;

        var tlsUpgraded = false;

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
            if (_socketConnection!.InnerSocket is INatsTlsUpgradeableSocketConnection tlsUpgradeableSocket and not INatsTlsSocketConnection)
            {
                var tlsMode = EffectiveTlsMode(_currentConnectUri.Uri);
                if (tlsMode == TlsMode.Disable && WritableServerInfo!.TlsRequired)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} requires TLS but TlsMode is set to Disable");
                }

                if (tlsMode == TlsMode.Require && !WritableServerInfo!.TlsRequired && !WritableServerInfo.TlsAvailable)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} does not support TLS but TLS is required ({TlsRequiredReason(_currentConnectUri.Uri)})");
                }

                if (tlsMode is TlsMode.Require or TlsMode.Prefer && (WritableServerInfo!.TlsRequired || WritableServerInfo.TlsAvailable))
                {
                    // do TLS upgrade
                    var targetUri = FixTlsHost(_currentConnectUri);

                    _logger.LogDebug(NatsLogEvents.Security, "Perform TLS Upgrade to {Uri}", targetUri);

                    // This INFO came in plaintext: anyone on the path could have rewritten it.
                    // Its connect_urls are not used.
                    tlsUpgraded = true;

                    // cancel INFO parsed signal and dispose current socket reader
                    infoParsedSignal.SetCanceled();
                    await _socketReader!.DisposeAsync().ConfigureAwait(false);
                    _socketReader = null;

                    // upgrade TcpConnection to SslConnection
                    var sslConnection = new SslStreamConnection(tlsUpgradeableSocket, Opts.TlsOpts);
                    await sslConnection.AuthenticateAsClientAsync(targetUri, Opts.ConnectTimeout).ConfigureAwait(false);
                    _socketConnection = _socketConnection with { InnerSocket = sslConnection };
                    _currentConnectionTls = true;

                    // create new socket reader
                    waitForPongOrErrorSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    infoParsedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _socketReader = new NatsReadProtocolProcessor(_socketConnection, this, waitForInfoSignal, waitForPongOrErrorSignal, infoParsedSignal.Task);
                }
            }

            // No TLS upgrade: this first INFO came over the connection's final transport.
            if (!tlsUpgraded)
                SetDiscoveredServers(WritableServerInfo!.ClientConnectUrls);

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

            // Queue the reply inbox SUB before the connection is reported open. Anything a caller
            // publishes once released is written to the same pipe after it, so a Direct mode
            // request cannot reach the server ahead of the subscription for its reply. On a
            // reconnect the inbox already exists and was replayed above, so this is a no-op.
            if (Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
            {
                await _subscriptionManager.InitializeInboxSubscriptionAsync(_disposedCts.Token).ConfigureAwait(false);
            }

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
            Exception? disconnectError = null;

            try
            {
                // wait for the current socket to complete or throw
                await _socketConnection!.WaitForClosed.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // If the NatsConnection is disposed, WaitForClosed throws so stop reconnect-loop correctly
                if (CheckDisposed())
                {
                    return;
                }

                // otherwise keep the cause for the disconnected event and reconnect.
                // Cancellation means this side closed the socket, so there is no error to report.
                if (ex is not OperationCanceledException)
                {
                    disconnectError = ex;
                }
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
            PushEvent(NatsEvent.ConnectionDisconnected, new NatsEventArgs(_currentConnectUri?.ToString() ?? string.Empty, disconnectError));

            // Cleanup current socket
            await DisposeSocketAsync(true).ConfigureAwait(false);

            // Always keep the original seed URLs in the list of URLs to connect to. A discovered
            // server that is also a configured one is dialled as configured.
            var seedUrls = Opts.GetSeedUris();
            var serverReportedUrls = GetServerReportedUris().Where(x => !seedUrls.Any(seed => seed.Host == x.Host && seed.Port == x.Port));
            var urls = serverReportedUrls.Concat(seedUrls).Distinct();
            if (!Opts.NoRandomize)
            {
                urls = urls.OrderBy(_ => Guid.NewGuid());
            }

            // Ensure the current URL is last in the list
            urls = urls.Where(x => x != _currentConnectUri).Append(_currentConnectUri!).ToArray();

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

                PushEvent(NatsEvent.ReconnectFailed, new NatsEventArgs(attempted?.ToString() ?? string.Empty));

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

            bool emitReconnect;
            string openedUrl;

            // OnConnectingAsync may have redirected the connection, so report the URI the socket was opened to.
            var connectedUri = _currentConnectUri ?? url;
            lock (_gate)
            {
                _connectRetry = 0;
                _backoff = TimeSpan.Zero;
                _logger.LogInformation(NatsLogEvents.Connection, "Connection succeeded {Name}, NATS {Url} [{ReconnectCount}]", _name, connectedUri, reconnectCount);
                ConnectionState = NatsConnectionState.Open;
                emitReconnect = Telemetry.Reconnects.Enabled;
                _pingTimerCancellationTokenSource = new CancellationTokenSource();
                StartPingTimer(_pingTimerCancellationTokenSource.Token);
                _waitForOpenConnection.TrySetResult();
                openedUrl = connectedUri.ToString();
            }

            // Queue the event outside the connection lock (see InitialConnectAsync).
            PushEvent(NatsEvent.ConnectionOpened, new NatsEventArgs(openedUrl));

            // The next reconnect cycle is started only after the open event is queued, so the
            // disconnect of the next cycle can never be reported before this open.
            _reconnectLoopTask = Task.Run(ReconnectLoop);

            if (emitReconnect)
                Telemetry.Reconnects.Add(1, Telemetry.BuildMetricTags(this, Telemetry.Constants.OpReconnect));
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
                    case NatsEvent.ServerError when ServerError != null && args is NatsServerErrorEventArgs error:
                        await ServerError.InvokeAsync(this, error).ConfigureAwait(false);
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

    // A tls:// or wss:// URL among the configured servers asks for TLS for every server.
    private TlsMode EffectiveTlsMode(Uri uri)
        => Opts.TlsOpts.EffectiveMode(uri, _tlsUrlConfigured ??= Opts.GetSeedUris(suppressRandomization: true).Any(x => NatsTlsOpts.IsTlsScheme(x.Uri)));

    // Why TLS is required for a URL, for error messages: Require is often derived, not set.
    private string TlsRequiredReason(Uri uri)
    {
        var mode = Opts.TlsOpts.Mode;
        if (mode is TlsMode.Require or TlsMode.Implicit)
            return $"TlsMode is set to {mode}";
        if (NatsTlsOpts.IsTlsScheme(uri))
            return $"the URL is {uri.Scheme}://";
        if (Opts.TlsOpts.HasTlsCerts)
            return "TLS certificates are configured";
        return "a tls:// or wss:// URL is among the configured servers";
    }

    private void SetDiscoveredServers(string[]? connectUrls)
    {
        // an INFO without connect_urls, or with an empty list, does not change the servers
        if (connectUrls is not { Length: > 0 })
            return;

        // Servers from INFO are dialled with the transport and the TLS of the connection they
        // were learned on: a server must not be able to choose the transport or turn TLS off.
        var webSocket = _currentConnectUri?.IsWebSocket == true;
        var scheme = _currentConnectionTls ? (webSocket ? "wss" : "tls") : (webSocket ? "ws" : NatsUri.DefaultScheme);

        lock (_connectUrlsGate)
        {
            var previous = _discoveredServers;
            var servers = new (string Entry, string Scheme)[connectUrls.Length];
            for (var i = 0; i < connectUrls.Length; i++)
            {
                // A server learned over TLS keeps the scheme it was learned with when it is
                // reported again without TLS. Each entry is read with the scheme of the
                // connection it came from, which decides its port when it has none.
                var entry = connectUrls[i];
                var key = ServerKey(entry, scheme);
                var learned = previous == null ? default : Array.Find(previous, x => x.Scheme is "tls" or "wss" && ServerKey(x.Entry, x.Scheme) == key);
                servers[i] = (entry, learned.Scheme ?? scheme);
            }

            _discoveredServers = servers;
        }
    }

    // Host and port as they would be dialled, so that another spelling of an entry (case,
    // leading zeros in the port) is the same server.
    private string ServerKey(string entry, string scheme)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return string.Empty;

        try
        {
            var uri = NatsUri.FromConnectUrl(entry, scheme);
            return $"{uri.Host}:{uri.Port}";
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return entry;
        }
    }

    private List<NatsUri> GetServerReportedUris()
    {
        var uris = new List<NatsUri>();
        (string Entry, string Scheme)[]? servers;
        lock (_connectUrlsGate)
            servers = _discoveredServers;
        if (servers == null)
            return uris;

        foreach (var (connectUrl, scheme) in servers)
        {
            // One bad entry must not cost the others, or stop the reconnect loop.
            if (string.IsNullOrWhiteSpace(connectUrl))
            {
                _logger.LogWarning(NatsLogEvents.Connection, "Ignoring empty server URL in INFO connect_urls");
                continue;
            }

            try
            {
                uris.Add(NatsUri.FromConnectUrl(connectUrl, scheme));
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                // the entry is server input: escape it so it can't forge log lines
                _logger.LogWarning(NatsLogEvents.Connection, e, "Ignoring invalid server URL in INFO connect_urls: {Url}", JsonEncodedText.Encode(connectUrl).ToString());
            }
        }

        return uris;
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

                try
                {
                    await PingOnlyAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (NatsTimeoutException)
                {
                    // Couldn't get the PING out (e.g. send buffer full on a dead connection).
                    // Count it as a missed PONG so MaxPingOut can still abort the connection.
                    _logger.LogDebug(NatsLogEvents.Connection, "Ping write timed out");
                }

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
            _socketReader.Detach();

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

    // Drain subs that already removed themselves from the SubscriptionManager
    // (e.g. JetStream Fetch on natural completion) but still have a user
    // iterator reading the channel. Their own DrainAsync waits on the
    // reader-exited signal. Run in parallel so a slow/dead server doesn't
    // multiply the dispose wait by the number of subs.
    private ValueTask DrainRemainingParticipantsAsync()
    {
        NatsSubBase[] remaining;
        lock (_drainParticipantsGate)
        {
            if (_drainParticipants.Count == 0)
                return default;
            remaining = _drainParticipants.ToArray();
        }

        var tasks = new Task[remaining.Length];
        for (var i = 0; i < remaining.Length; i++)
            tasks[i] = remaining[i].DrainOnDisposeAsync().AsTask();

        return new ValueTask(Task.WhenAll(tasks));
    }
}
