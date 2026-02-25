using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class NatsServer : IAsyncDisposable
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string Ext = IsWindows ? ".exe" : string.Empty;
    private static readonly string NatsServerPath = $"nats-server{Ext}";

    private readonly string? _jetStreamStoreDir;
    private readonly ITestOutputHelper _outputHelper;
    private readonly TransportType _transportType;
    private readonly OutputHelperLoggerFactory _loggerFactory;
    private CancellationTokenSource? _cancellationTokenSource;
    private int _disposed;
    private volatile Process? _serverProcess;

    private NatsServer(ITestOutputHelper outputHelper, NatsServerOpts opts)
    {
        _outputHelper = outputHelper;
        _transportType = opts.TransportType;
        Opts = opts;
        _loggerFactory = new OutputHelperLoggerFactory(_outputHelper, this);

        if (opts.EnableJetStream)
        {
            _jetStreamStoreDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_jetStreamStoreDir);
            opts.JetStreamStoreDir = _jetStreamStoreDir;
        }
    }

    public string? ConfigFile { get; private set; }

    public Process? ServerProcess
    {
        get => Interlocked.CompareExchange(ref _serverProcess, null, null);
        private set => Interlocked.Exchange(ref _serverProcess, value);
    }

    public NatsServerOpts Opts { get; }

    public string ClientUrl => _transportType switch
    {
        TransportType.Tcp => $"nats://127.0.0.1:{Opts.ServerPort}",
        TransportType.Tls => $"tls://127.0.0.1:{Opts.ServerPort}",
        TransportType.WebSocket => $"ws://127.0.0.1:{Opts.WebSocketPort}",
        TransportType.WebSocketSecure => $"wss://127.0.0.1:{Opts.WebSocketPort}",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public string ClientUrlWithAuth
    {
        get
        {
            if (!string.IsNullOrEmpty(Opts.ClientUrlUserName))
            {
                var uriBuilder = new UriBuilder(ClientUrl);
                uriBuilder.UserName = Opts.ClientUrlUserName;
                uriBuilder.Password = Opts.ClientUrlPassword;
                return uriBuilder.ToString().TrimEnd('/');
            }

            return ClientUrl;
        }
    }

    public int ConnectionPort
    {
        get
        {
            if (_transportType is TransportType.WebSocket or TransportType.WebSocketSecure && ServerVersions.V2_9_19 <= NatsServerExe.Version)
            {
                return Opts.WebSocketPort!.Value;
            }
            else
            {
                return Opts.ServerPort;
            }
        }
    }

    public Action<LogMessage> OnLog { get; set; } = _ => { };

    public static Task<NatsServer> StartJSAsync() => StartJSAsync(new NullOutputHelper(), TransportType.Tcp);

    public static Task<NatsServer> StartJSWithTraceAsync(ITestOutputHelper outputHelper) => StartAsync(
        outputHelper: outputHelper,
        opts: new NatsServerOptsBuilder()
            .UseTransport(TransportType.Tcp)
            .Trace()
            .UseJetStream()
            .Build());

    public static Task<NatsServer> StartJSAsync(ITestOutputHelper outputHelper, TransportType transportType) => StartAsync(
        outputHelper: outputHelper,
        opts: new NatsServerOptsBuilder()
            .UseTransport(transportType)
            .UseJetStream()
            .Build());

    public static async Task<NatsServer> StartAsync() => await StartAsync(new NullOutputHelper(), TransportType.Tcp);

    public static Task<NatsServer> StartWithTraceAsync(ITestOutputHelper outputHelper)
        => StartAsync(
            outputHelper,
            new NatsServerOptsBuilder()
                .Trace()
                .UseTransport(TransportType.Tcp)
                .Build());

    public static async Task<NatsServer> StartAsync(ITestOutputHelper outputHelper, TransportType transportType) =>
        await StartAsync(outputHelper, new NatsServerOptsBuilder().UseTransport(transportType).Build());

    public static async Task<NatsServer> StartAsync(ITestOutputHelper outputHelper, NatsServerOpts opts, NatsOpts? clientOpts = default, bool useAuthInUrl = false)
    {
        DebugLogger.Log("[T] Starting NATS server");

        NatsServer? server = null;
        NatsConnection? nats = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                server = new NatsServer(outputHelper, opts);
                await server.StartServerProcessAsync();
                nats = await server.CreateClientConnectionAsync(clientOpts ?? NatsOpts.Default, reTryCount: 3, useAuthInUrl: useAuthInUrl);
                return server;
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[T] Starting process error: {e}");
                if (server != null)
                {
                    await server.DisposeAsync();
                }
            }
            finally
            {
                if (nats != null)
                {
                    await nats.DisposeAsync();
                }
            }
        }

        throw new Exception("Can't start nats-server and connect to it");
    }

    public async Task StartServerProcessAsync()
    {
        DebugLogger.Log("[T] Starting process");
        _cancellationTokenSource = new CancellationTokenSource();

        (ConfigFile, var config, var args) = GetCmd(Opts);

        _outputHelper.WriteLine($"ProcessStart: {NatsServerPath} {args}{Environment.NewLine}{config}");
        var info = new ProcessStartInfo
        {
            FileName = NatsServerPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var process = new Process { StartInfo = info, };
        var listening = new TaskCompletionSource();
        DataReceivedEventHandler? outputHandler = (_, e) =>
        {
            if (e.Data != null)
            {
                if (e.Data.Contains("Listening for client connections"))
                {
                    listening.TrySetResult();
                }

                DebugLogger.Log($"  | {e.Data}", 2);
            }
        };
        process.OutputDataReceived += outputHandler;
        process.ErrorDataReceived += outputHandler;
        process.Start();
        if (IsWindows)
            ChildProcessTracker.AddProcess(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await listening.Task.WaitAsync(TimeSpan.FromSeconds(3), _cancellationTokenSource.Token);

        ServerProcess = process;

        DebugLogger.Log($"[T] Started process {process.Id} / {Process.GetProcessById(process.Id).ProcessName}");

        // Check for start server
        var loopTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loopCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, loopTimeoutCts.Token);
        using var client = new TcpClient();
        var connected = false;
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                DebugLogger.Log($"[T] TCP Connect to {Opts.ServerPort}");
                var attemptTimeoutCts = new CancellationTokenSource(250);
                var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(loopCts.Token, attemptTimeoutCts.Token);
                await client.ConnectAsync("127.0.0.1", Opts.ServerPort).WaitAsync(attemptCts.Token);
                if (client.Connected)
                {
                    connected = true;
                    break;
                }
            }
            catch
            {
                // ignore
            }

            await Task.Delay(250, loopCts.Token);
        }

        if (process.HasExited)
        {
            throw new Exception($"NATS server exited with code {ServerProcess.ExitCode}");
        }

        if (!connected)
        {
            throw new Exception($"NATS server not connected");
        }

        _outputHelper.WriteLine("OK to Process Start, Port:" + Opts.ServerPort);
    }

    public async ValueTask RestartAsync()
    {
        DebugLogger.Log("[T] Restarting process");

        var serverProcessId = ServerProcess?.Id;

        await StopAsync();

        try
        {
            if (serverProcessId != null)
            {
                Process.GetProcessById(serverProcessId.Value).Kill();
                Process.GetProcessById(serverProcessId.Value).WaitForExit(10_000);
            }
        }
        catch
        {
            // ignore
        }

        await StartServerProcessAsync();

        if (serverProcessId == ServerProcess!.Id)
            throw new Exception("Can't restart nats-server");
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async ValueTask StopAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        DebugLogger.Log("[T] Stopping process");
        try
        {
            _cancellationTokenSource?.Cancel(); // trigger of process kill.
            _cancellationTokenSource?.Dispose();
            if (ServerProcess != null)
            {
                ServerProcess.Kill();
                ServerProcess.WaitForExit(10_000);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async ValueTask DisposeAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        DebugLogger.Log("[T] Disposing server");

        if (Interlocked.Increment(ref _disposed) != 1)
        {
            return;
        }

        try
        {
            _cancellationTokenSource?.Cancel(); // trigger of process kill.
            _cancellationTokenSource?.Dispose();
            if (ServerProcess != null)
            {
                ServerProcess.Kill();
                ServerProcess.WaitForExit(10_000);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (ConfigFile != null)
            {
                File.Delete(ConfigFile);
            }

            if (_jetStreamStoreDir != null)
            {
                try
                {
                    Directory.Delete(_jetStreamStoreDir, true);
                }
                catch
                {
                    /* best effort */
                }
            }

            if (Opts.ServerDisposeReturnsPorts)
            {
                Opts.Dispose();
            }
        }
    }

    public (NatsConnection, NatsProxy) CreateProxiedClientConnection(NatsOpts? options = null)
    {
        if (Opts.EnableTls)
        {
            throw new Exception("Tapped mode doesn't work wit TLS");
        }

        var proxy = new NatsProxy(Opts.ServerPort, _outputHelper, Opts.Trace);

        var client = new NatsConnection((options ?? NatsOpts.Default) with
        {
            LoggerFactory = _loggerFactory,
            Url = $"nats://127.0.0.1:{proxy.Port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });

        return (client, proxy);
    }

    public async Task<NatsConnection> CreateClientConnectionAsync(NatsOpts? options = default, int reTryCount = 10, bool ignoreAuthorizationException = false, bool testLogger = true, bool useAuthInUrl = false)
    {
        for (var i = 0; i < reTryCount; i++)
        {
            try
            {
                var nats = new NatsConnection(ClientOpts(options ?? NatsOpts.Default, testLogger: testLogger, useAuthInUrl: useAuthInUrl));

                try
                {
                    await nats.PingAsync();
                }
                catch (NatsException e)
                {
                    if (!ignoreAuthorizationException)
                        throw;

                    if (e.GetBaseException().Message == "Authorization Violation")
                        return nats;
                }

                return nats;
            }
            catch
            {
                // ignore
            }
        }

        throw new Exception("Can't create a connection to nats-server");
    }

    public NatsConnectionPool CreatePooledClientConnection() => CreatePooledClientConnection(NatsOpts.Default);

    public NatsConnectionPool CreatePooledClientConnection(NatsOpts opts)
    {
        return new NatsConnectionPool(4, ClientOpts(opts));
    }

    public NatsOpts ClientOpts(NatsOpts opts, bool testLogger = true, bool useAuthInUrl = false)
    {
        var natsTlsOpts = Opts.EnableTls
            ? opts.TlsOpts with
            {
#if NET8_0_OR_GREATER
                CertFile = Opts.TlsClientCertFile,
                KeyFile = Opts.TlsClientKeyFile,
#endif
                CaFile = Opts.TlsCaFile,
                Mode = Opts.TlsFirst ? TlsMode.Implicit : TlsMode.Auto,
            }
            : NatsTlsOpts.Default;

        return opts with
        {
            LoggerFactory = testLogger ? _loggerFactory : opts.LoggerFactory,
            TlsOpts = natsTlsOpts,
            Url = useAuthInUrl ? ClientUrlWithAuth : ClientUrl,
        };
    }

    public void LogMessage<TState>(string categoryName, LogLevel logLevel, EventId eventId, Exception? exception, string text, TState state)
    {
        foreach (var @delegate in OnLog.GetInvocationList())
        {
            var action = (Action<LogMessage>)@delegate;
            try
            {
                if (state is IReadOnlyList<KeyValuePair<string, object?>> kvs)
                {
                    action(new LogMessage(categoryName, logLevel, eventId, exception, text, kvs));
                }
                else
                {
                    action(new LogMessage(categoryName, logLevel, eventId, exception, text, new[]
                    {
                        new KeyValuePair<string, object?>("text", text),
                    }));
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static (string configFileName, string config, string args) GetCmd(NatsServerOpts opts)
    {
        var configFileName = Path.GetTempFileName();

        var config = opts.ConfigFileContents;
        File.WriteAllText(configFileName, config);

        var args = $"-c {configFileName}";

        return (configFileName, config, args);
    }
}

public record LogMessage(
    string Category,
    LogLevel LogLevel,
    EventId EventId,
    Exception? Exception,
    string Text,
    IReadOnlyList<KeyValuePair<string, object?>> State);

public class NatsCluster : IAsyncDisposable
{
    private readonly NatsServerOpts _opts1;

    private readonly NatsServerOpts _opts2;

    private readonly NatsServerOpts _opts3;

    private readonly ITestOutputHelper _outputHelper;

    private readonly bool _useAuthInUrl;

    private NatsServer? _server1;

    private NatsServer? _server2;

    private NatsServer? _server3;

    public NatsCluster(ITestOutputHelper outputHelper, TransportType transportType, Action<int, NatsServerOptsBuilder>? configure = default, bool useAuthInUrl = false)
    {
        _outputHelper = outputHelper;
        _useAuthInUrl = useAuthInUrl;

        var builder1 = new NatsServerOptsBuilder()
            .UseTransport(transportType)
            .EnableClustering();
        configure?.Invoke(1, builder1);
        _opts1 = builder1.Build();

        var builder2 = new NatsServerOptsBuilder()
            .UseTransport(transportType)
            .EnableClustering();
        configure?.Invoke(2, builder2);
        _opts2 = builder2.Build();

        var builder3 = new NatsServerOptsBuilder()
            .UseTransport(transportType)
            .EnableClustering();
        configure?.Invoke(3, builder3);
        _opts3 = builder3.Build();

        // By querying the ports we set the values lazily on all the opts.
        outputHelper.WriteLine($"opts1.ServerPort={_opts1.ServerPort}");
        outputHelper.WriteLine($"opts1.ClusteringPort={_opts1.ClusteringPort}");
        if (_opts1.EnableWebSocket)
        {
            outputHelper.WriteLine($"opts1.WebSocketPort={_opts1.WebSocketPort}");
        }

        outputHelper.WriteLine($"opts2.ServerPort={_opts2.ServerPort}");
        outputHelper.WriteLine($"opts2.ClusteringPort={_opts2.ClusteringPort}");
        if (_opts2.EnableWebSocket)
        {
            outputHelper.WriteLine($"opts2.WebSocketPort={_opts2.WebSocketPort}");
        }

        outputHelper.WriteLine($"opts3.ServerPort={_opts3.ServerPort}");
        outputHelper.WriteLine($"opts3.ClusteringPort={_opts3.ClusteringPort}");
        if (_opts3.EnableWebSocket)
        {
            outputHelper.WriteLine($"opts3.WebSocketPort={_opts3.WebSocketPort}");
        }

        var routes = new[] { _opts1, _opts2, _opts3 };
        foreach (var opt in routes)
        {
            opt.SetRoutes(routes);
        }
    }

    public NatsServer Server1 => _server1 ?? throw new InvalidOperationException("call StartAsync");

    public NatsServer Server2 => _server2 ?? throw new InvalidOperationException("call StartAsync");

    public NatsServer Server3 => _server3 ?? throw new InvalidOperationException("call StartAsync");

    public async Task StartAsync()
    {
        if (_server1 == null)
        {
            _outputHelper.WriteLine("Starting server 1...");
            _server1 = await NatsServer.StartAsync(_outputHelper, _opts1, useAuthInUrl: _useAuthInUrl);
        }

        if (_server2 == null)
        {
            _outputHelper.WriteLine("Starting server 2...");
            _server2 = await NatsServer.StartAsync(_outputHelper, _opts2, useAuthInUrl: _useAuthInUrl);
        }

        if (_server3 == null)
        {
            _outputHelper.WriteLine("Starting server 3...");
            _server3 = await NatsServer.StartAsync(_outputHelper, _opts3, useAuthInUrl: _useAuthInUrl);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server1 != null)
        {
            _outputHelper.WriteLine("Stopping server 1...");
            await _server1.DisposeAsync();
            _server1 = null;
        }

        if (_server2 != null)
        {
            _outputHelper.WriteLine("Stopping server 2...");
            await _server2.DisposeAsync();
            _server2 = null;
        }

        if (_server3 != null)
        {
            _outputHelper.WriteLine("Stopping server 3...");
            await _server3.DisposeAsync();
            _server3 = null;
        }
    }
}

#pragma warning disable CS4014

