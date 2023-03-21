using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cysharp.Diagnostics;

namespace NATS.Client.Core.Tests;

public class NatsServer : IAsyncDisposable
{
    private static readonly string Ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;
    private static readonly string NatsServerPath = $"nats-server{Ext}";

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string? _configFileName;
    private readonly ITestOutputHelper _outputHelper;
    private readonly Task<string[]> _processOut;
    private readonly Task<string[]> _processErr;
    private readonly TransportType _transportType;
    private int _disposed;

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType)
        : this(outputHelper, transportType, new NatsServerOptionsBuilder().UseTransport(transportType).Build())
    {
    }

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType, NatsServerOptions options)
    {
        _outputHelper = outputHelper;
        _transportType = transportType;
        Options = options;
        _configFileName = Path.GetTempFileName();
        var config = options.ConfigFileContents;
        File.WriteAllText(_configFileName, config);
        var cmd = $"{NatsServerPath} -c {_configFileName}";

        outputHelper.WriteLine("ProcessStart: " + cmd + Environment.NewLine + config);
        var (p, stdout, stderr) = ProcessX.GetDualAsyncEnumerable(cmd);

        _processOut = EnumerateWithLogsAsync(stdout, _cancellationTokenSource.Token);
        _processErr = EnumerateWithLogsAsync(stderr, _cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Options.ServerPort, _cancellationTokenSource.Token);
                    if (client.Connected)
                        return;
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(500, _cancellationTokenSource.Token);
            }
        }).Wait(5000); // timeout

        if (_processOut.IsFaulted)
        {
            _processOut.GetAwaiter().GetResult(); // throw exception
        }

        if (_processErr.IsFaulted)
        {
            _processErr.GetAwaiter().GetResult(); // throw exception
        }

        outputHelper.WriteLine("OK to Process Start, Port:" + Options.ServerPort);
    }

    public NatsServerOptions Options { get; }

    public string ClientUrl => _transportType switch
    {
        TransportType.Tcp => $"nats://localhost:{Options.ServerPort}",
        TransportType.Tls => $"tls://localhost:{Options.ServerPort}",
        TransportType.WebSocket => $"ws://localhost:{Options.WebSocketPort}",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) != 1)
        {
            return;
        }

        _cancellationTokenSource.Cancel(); // trigger of process kill.
        _cancellationTokenSource.Dispose();
        try
        {
            var processLogs = await _processErr; // wait for process exit, nats output info to stderror
            if (processLogs.Length != 0)
            {
                _outputHelper.WriteLine("Process Logs of " + Options.ServerPort);
                foreach (var item in processLogs)
                {
                    _outputHelper.WriteLine(item);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_configFileName != null)
            {
                File.Delete(_configFileName);
            }

            if (Options.ServerDisposeReturnsPorts)
            {
                Options.Dispose();
            }
        }
    }

    public NatsConnection CreateClientConnection() => CreateClientConnection(NatsOptions.Default);

    public NatsConnection CreateClientConnection(NatsOptions options)
    {
        return new NatsConnection(ClientOptions(options));
    }

    public NatsConnectionPool CreatePooledClientConnection() => CreatePooledClientConnection(NatsOptions.Default);

    public NatsConnectionPool CreatePooledClientConnection(NatsOptions options)
    {
        return new NatsConnectionPool(4, ClientOptions(options));
    }

    public NatsOptions ClientOptions(NatsOptions options)
    {
        return options with
        {
            LoggerFactory = new OutputHelperLoggerFactory(_outputHelper),

            // ConnectTimeout = TimeSpan.FromSeconds(1),
            // ReconnectWait = TimeSpan.Zero,
            // ReconnectJitter = TimeSpan.Zero,
            TlsOptions = Options.EnableTls
                ? TlsOptions.Default with
                {
                    CertFile = Options.TlsClientCertFile,
                    KeyFile = Options.TlsClientKeyFile,
                    CaFile = Options.TlsCaFile,
                }
                : TlsOptions.Default,
            Url = ClientUrl,
        };
    }

    private async Task<string[]> EnumerateWithLogsAsync(ProcessAsyncEnumerable enumerable, CancellationToken cancellationToken)
    {
        var l = new List<string>();
        try
        {
            await foreach (var item in enumerable.WithCancellation(cancellationToken))
            {
                l.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return l.ToArray();
    }
}

public class NatsCluster : IAsyncDisposable
{
    public NatsCluster(ITestOutputHelper outputHelper, TransportType transportType)
    {
        var opts1 = new NatsServerOptions
        {
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var opts2 = new NatsServerOptions
        {
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var opts3 = new NatsServerOptions
        {
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var routes = new[] { opts1, opts2, opts3 };
        foreach (var opt in routes)
        {
            opt.SetRoutes(routes);
        }

        Server1 = new NatsServer(outputHelper, transportType, opts1);
        Server2 = new NatsServer(outputHelper, transportType, opts2);
        Server3 = new NatsServer(outputHelper, transportType, opts3);
    }

    public NatsServer Server1 { get; }

    public NatsServer Server2 { get; }

    public NatsServer Server3 { get; }

    public async ValueTask DisposeAsync()
    {
        await Server1.DisposeAsync();
        await Server2.DisposeAsync();
        await Server3.DisposeAsync();
    }
}
