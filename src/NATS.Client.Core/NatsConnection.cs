using System.Buffers;
using System.Diagnostics;
using System.Text;
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

public partial class NatsConnection : IAsyncDisposable, INatsCommand
{
    /// <summary>
    /// Hook before TCP connection open.
    /// </summary>
    public Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync;

    internal readonly ConnectionStatsCounter Counter; // allow to call from external sources
    internal readonly ReadOnlyMemory<byte> InboxPrefix;
    private readonly object _gate = new object();
    private readonly WriterState _writerState;
    private readonly ChannelWriter<ICommand> _commandWriter;
    private readonly SubscriptionManager _subscriptionManager;
    private readonly RequestResponseManager _requestResponseManager;
    private readonly ILogger<NatsConnection> _logger;
    private readonly ObjectPool _pool;
    private readonly string _name;
    private readonly TimeSpan _socketComponentDisposeTimeout = TimeSpan.FromSeconds(5);

    private int _pongCount;
    private bool _isDisposed;

    // when reconnect, make new instance.
    private ISocketConnection? _socket;
    private CancellationTokenSource? _pingTimerCancellationTokenSource;
    private NatsUri? _currentConnectUri;
    private NatsUri? _lastSeedConnectUri;
    private NatsReadProtocolProcessor? _socketReader;
    private NatsPipeliningWriteProtocolProcessor? _socketWriter;
    private TaskCompletionSource _waitForOpenConnection;
    private TlsCerts? _tlsCerts;
    private ClientOptions _clientOptions;
    private UserCredentials? _userCredentials;

    public NatsConnection()
        : this(NatsOptions.Default)
    {
    }

    public NatsConnection(NatsOptions options)
    {
        Options = options;
        ConnectionState = NatsConnectionState.Closed;
        _waitForOpenConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pool = new ObjectPool(options.CommandPoolSize);
        _name = options.Name;
        Counter = new ConnectionStatsCounter();
        _writerState = new WriterState(options);
        _commandWriter = _writerState.CommandBuffer.Writer;
        _subscriptionManager = new SubscriptionManager(this);
        _requestResponseManager = new RequestResponseManager(this, _pool);
        InboxPrefix = Encoding.ASCII.GetBytes($"{options.InboxPrefix}{Guid.NewGuid()}.");
        _logger = options.LoggerFactory.CreateLogger<NatsConnection>();
        _clientOptions = new ClientOptions(Options);
    }

    // events
    public event EventHandler<string>? ConnectionDisconnected;

    public event EventHandler<string>? ConnectionOpened;

    public event EventHandler<string>? ReconnectFailed;

    public NatsOptions Options { get; }

    public NatsConnectionState ConnectionState { get; private set; }

    public ServerInfo? ServerInfo { get; internal set; } // server info is set when received INFO

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public async ValueTask ConnectAsync()
    {
        if (ConnectionState == NatsConnectionState.Open) return;

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
        else
        {
            // Only Closed(initial) state, can run initial connect.
            await InitialConnectAsync().ConfigureAwait(false);
        }
    }

    public NatsStats GetStats() => Counter.ToStats();

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _logger.Log(LogLevel.Information, $"Disposing connection {_name}.");

            await DisposeSocketAsync(false).ConfigureAwait(false);
            _pingTimerCancellationTokenSource?.Cancel();
            foreach (var item in _writerState.PendingPromises)
            {
                item.SetCanceled(CancellationToken.None);
            }

            _subscriptionManager.Dispose();
            _requestResponseManager.Dispose();
            _waitForOpenConnection.TrySetCanceled();
        }
    }

    internal void EnqueuePing(AsyncPingCommand pingCommand)
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
        pingCommand.SetCanceled(CancellationToken.None);
    }

    internal void PostPong()
    {
        EnqueueCommand(PongCommand.Create(_pool));
    }

    internal ValueTask SubscribeAsync(int subscriptionId, string subject, in NatsKey? queueGroup)
    {
        var command = AsyncSubscribeCommand.Create(_pool, subscriptionId, new NatsKey(subject, true), queueGroup);
        EnqueueCommand(command);
        return command.AsValueTask();
    }

    internal void PostUnsubscribe(int subscriptionId)
    {
        EnqueueCommand(UnsubscribeCommand.Create(_pool, subscriptionId));
    }

    internal void PostCommand(ICommand command)
    {
        EnqueueCommand(command);
    }

    internal void PublishToClientHandlers(int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        _subscriptionManager.PublishToClientHandlers(subscriptionId, buffer);
    }

    internal void PublishToRequestHandler(int subscriptionId, in NatsKey replyTo, in ReadOnlySequence<byte> buffer)
    {
        _subscriptionManager.PublishToRequestHandler(subscriptionId, replyTo, buffer);
    }

    internal void PublishToResponseHandler(int requestId, in ReadOnlySequence<byte> buffer)
    {
        _requestResponseManager.PublishToResponseHandler(requestId, buffer);
    }

    internal void ResetPongCount()
    {
        Interlocked.Exchange(ref _pongCount, 0);
    }

    private async ValueTask InitialConnectAsync()
    {
        Debug.Assert(ConnectionState == NatsConnectionState.Connecting);

        var uris = Options.GetSeedUris();
        if (Options.TlsOptions.Disabled && uris.Any(u => u.IsTls))
            throw new NatsException($"URI {uris.First(u => u.IsTls)} requires TLS but TlsOptions.Disabled is set to true");
        if (Options.TlsOptions.Required)
            _tlsCerts = new TlsCerts(Options.TlsOptions);

        if (!Options.AuthOptions.IsAnonymous)
        {
            _userCredentials = new UserCredentials(Options.AuthOptions);
        }

        foreach (var uri in uris)
        {
            try
            {
                var target = (uri.Host, uri.Port);
                if (OnConnectingAsync != null)
                {
                    _logger.LogInformation("Try to invoke OnConnectingAsync before connect to NATS.");
                    target = await OnConnectingAsync(target).ConfigureAwait(false);
                }

                _logger.LogInformation("Try to connect NATS {0}", uri);
                if (uri.IsWebSocket)
                {
                    var conn = new WebSocketConnection();
                    await conn.ConnectAsync(uri.Uri, Options.ConnectTimeout).ConfigureAwait(false);
                    _socket = conn;
                }
                else
                {
                    var conn = new TcpConnection();
                    await conn.ConnectAsync(target.Host, target.Port, Options.ConnectTimeout).ConfigureAwait(false);
                    _socket = conn;
                }

                _currentConnectUri = uri;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fail to connect NATS {0}", uri);
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
            _logger.LogInformation("Connect succeed {0}, NATS {1}", _name, url);
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
                if (Options.TlsOptions.Disabled && ServerInfo!.TlsRequired)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} requires TLS but TlsOptions.Disabled is set to true");
                }

                if (Options.TlsOptions.Required && !ServerInfo!.TlsRequired && !ServerInfo.TlsAvailable)
                {
                    throw new NatsException(
                        $"Server {_currentConnectUri} does not support TLS but TlsOptions.Disabled is set to true");
                }

                if (Options.TlsOptions.Required || ServerInfo!.TlsRequired || ServerInfo.TlsAvailable)
                {
                    // do TLS upgrade
                    // if the current URI is not a seed URI and is not a DNS hostname, check the server cert against the
                    // last seed hostname if it was a DNS hostname
                    var targetHost = _currentConnectUri.Host;
                    if (!_currentConnectUri.IsSeed
                        && Uri.CheckHostName(targetHost) != UriHostNameType.Dns
                        && Uri.CheckHostName(_lastSeedConnectUri!.Host) == UriHostNameType.Dns)
                    {
                        targetHost = _lastSeedConnectUri.Host;
                    }

                    _logger.LogDebug("Perform TLS Upgrade to " + targetHost);

                    // cancel INFO parsed signal and dispose current socket reader
                    infoParsedSignal.SetCanceled();
                    await _socketReader!.DisposeAsync().ConfigureAwait(false);
                    _socketReader = null;

                    // upgrade TcpConnection to SslConnection
                    var sslConnection = tcpConnection.UpgradeToSslStreamConnection(Options.TlsOptions, _tlsCerts);
                    await sslConnection.AuthenticateAsClientAsync(targetHost).ConfigureAwait(false);
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
            _userCredentials?.Authenticate(_clientOptions, ServerInfo);

            // add CONNECT and PING command to priority lane
            _writerState.PriorityCommands.Clear();
            var connectCommand = AsyncConnectCommand.Create(_pool, _clientOptions);
            _writerState.PriorityCommands.Add(connectCommand);
            _writerState.PriorityCommands.Add(PingCommand.Create(_pool));

            if (reconnect)
            {
                // Add SUBSCRIBE command to priority lane
                var subscribeCommand =
                    AsyncSubscribeBatchCommand.Create(_pool, _subscriptionManager.GetExistingSubscriptions());
                _writerState.PriorityCommands.Add(subscribeCommand);
            }

            // create the socket writer
            _socketWriter = new NatsPipeliningWriteProtocolProcessor(_socket!, _writerState, _pool, Counter);

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

    private async void ReconnectLoop()
    {
        try
        {
            // If dispose this client, WaitForClosed throws OperationCanceledException so stop reconnect-loop correctly.
            await _socket!.WaitForClosed.ConfigureAwait(false);

            _logger.LogTrace($"Detect connection {_name} closed, start to cleanup current connection and start to reconnect.");
            lock (_gate)
            {
                ConnectionState = NatsConnectionState.Reconnecting;
                _waitForOpenConnection.TrySetCanceled();
                _waitForOpenConnection = new TaskCompletionSource();
                _pingTimerCancellationTokenSource?.Cancel();
                _requestResponseManager.Reset();
            }

            // Invoke after state changed
            ConnectionDisconnected?.Invoke(this, _currentConnectUri?.ToString() ?? string.Empty);

            // Cleanup current socket
            await DisposeSocketAsync(true).ConfigureAwait(false);

            var defaultScheme = _currentConnectUri!.Uri.Scheme;
            var urls = (Options.NoRandomize
                ? ServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, false, defaultScheme)).Distinct().ToArray()
                : ServerInfo?.ClientConnectUrls?.Select(x => new NatsUri(x, false, defaultScheme)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray())
                    ?? Array.Empty<NatsUri>();
            if (urls.Length == 0)
                urls = Options.GetSeedUris();

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
                    var target = (url.Host, url.Port);
                    if (OnConnectingAsync != null)
                    {
                        _logger.LogInformation("Try to invoke OnConnectingAsync before connect to NATS.");
                        target = await OnConnectingAsync(target).ConfigureAwait(false);
                    }

                    _logger.LogInformation("Try to connect NATS {0}", url);
                    if (url.IsWebSocket)
                    {
                        var conn = new WebSocketConnection();
                        await conn.ConnectAsync(url.Uri, Options.ConnectTimeout).ConfigureAwait(false);
                        _socket = conn;
                    }
                    else
                    {
                        var conn = new TcpConnection();
                        await conn.ConnectAsync(target.Host, target.Port, Options.ConnectTimeout).ConfigureAwait(false);
                        _socket = conn;
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
                    _logger.LogError(ex, "Fail to connect NATS {0}", url);
                }

                ReconnectFailed?.Invoke(this, url?.ToString() ?? string.Empty);
                await WaitWithJitterAsync().ConfigureAwait(false);
                goto CONNECT_AGAIN;
            }

            lock (_gate)
            {
                _logger.LogInformation("Connect succeed {0}, NATS {1}", _name, url);
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
            if (ex is OperationCanceledException) return;
            _logger.LogError(ex, "Unknown error, loop stopped and connection is invalid state.");
        }
    }

    private async Task WaitWithJitterAsync()
    {
        var jitter = Random.Shared.NextDouble() * Options.ReconnectJitter.TotalMilliseconds;
        var waitTime = Options.ReconnectWait + TimeSpan.FromMilliseconds(jitter);
        if (waitTime != TimeSpan.Zero)
        {
            _logger.LogTrace("Wait {0}ms to reconnect.", waitTime.TotalMilliseconds);
            await Task.Delay(waitTime).ConfigureAwait(false);
        }
    }

    private async void StartPingTimer(CancellationToken cancellationToken)
    {
        if (Options.PingInterval == TimeSpan.Zero) return;

        var periodicTimer = new PeriodicTimer(Options.PingInterval);
        ResetPongCount();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref _pongCount) > Options.MaxPingOut)
                {
                    _logger.LogInformation("Detect MaxPingOut, try to connection abort.");
                    if (_socket != null)
                    {
                        await _socket.AbortConnectionAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                PostPing();
                await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    // internal commands.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void EnqueueCommand(ICommand command)
    {
        if (_commandWriter.TryWrite(command))
        {
            Interlocked.Increment(ref Counter.PendingMessages);
        }
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
            _logger.LogError(ex, $"Error occured when disposing {description}.");
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
        if (_isDisposed) throw new ObjectDisposedException(null);
    }

    private async void WithConnect(Action<NatsConnection> core)
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

    private async ValueTask WithConnectAsync(Func<NatsConnection, ValueTask> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        await coreAsync(this).ConfigureAwait(false);
    }

    private async ValueTask WithConnectAsync<T1>(T1 item1, Func<NatsConnection, T1, ValueTask> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        await coreAsync(this, item1).ConfigureAwait(false);
    }

    private async ValueTask WithConnectAsync<T1, T2>(T1 item1, T2 item2, Func<NatsConnection, T1, T2, ValueTask> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        await coreAsync(this, item1, item2).ConfigureAwait(false);
    }

    private async ValueTask<T> WithConnectAsync<T>(Func<NatsConnection, ValueTask<T>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this).ConfigureAwait(false);
    }

    private async ValueTask<TResult> WithConnectAsync<T1, T2, TResult>(T1 item1, T2 item2, Func<NatsConnection, T1, T2, ValueTask<TResult>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this, item1, item2).ConfigureAwait(false);
    }

    private async ValueTask<TResult> WithConnectAsync<T1, T2, T3, TResult>(T1 item1, T2 item2, T3 item3, Func<NatsConnection, T1, T2, T3, ValueTask<TResult>> coreAsync)
    {
        await ConnectAsync().ConfigureAwait(false);
        return await coreAsync(this, item1, item2, item3).ConfigureAwait(false);
    }
}

// This writer state is reused when reconnecting.
internal sealed class WriterState
{
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

    public FixedArrayBufferWriter BufferWriter { get; }

    public Channel<ICommand> CommandBuffer { get; }

    public NatsOptions Options { get; }

    public List<ICommand> PriorityCommands { get; }

    public List<IPromise> PendingPromises { get; }
}
