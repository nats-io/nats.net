using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Cysharp.Diagnostics;

namespace NATS.Client.Core.Tests;

public static class ServerVersions
{
#pragma warning disable SA1310
#pragma warning disable SA1401

    // Changed INFO port reporting for WS connections (nats-server #4255)
    public static Version V2_9_19 = new("2.9.19");

#pragma warning restore SA1401
#pragma warning restore SA1310
}

public class NatsServer : IAsyncDisposable
{
    private static readonly string Ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;
    private static readonly string NatsServerPath = $"nats-server{Ext}";
    private static readonly Version Version;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string? _configFileName;
    private readonly string? _jetStreamStoreDir;
    private readonly ITestOutputHelper _outputHelper;
    private readonly Task<string[]> _processOut;
    private readonly Task<string[]> _processErr;
    private readonly TransportType _transportType;
    private int _disposed;

    static NatsServer()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = NatsServerPath,
                Arguments = "-v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.Start();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var value = Regex.Match(output, @"v(\d+\.\d+\.\d+)").Groups[1].Value;
        Version = new Version(value);
    }

    private NatsServer(ITestOutputHelper outputHelper, NatsServerOptions options)
    {
        _outputHelper = outputHelper;
        _transportType = options.TransportType;
        Options = options;

        if (options.EnableJetStream)
        {
            _jetStreamStoreDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_jetStreamStoreDir);
            options.JetStreamStoreDir = _jetStreamStoreDir;
        }

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

    public int ConnectionPort
    {
        get
        {
            if (_transportType == TransportType.WebSocket && ServerVersions.V2_9_19 <= Version)
            {
                return Options.WebSocketPort!.Value;
            }
            else
            {
                return Options.ServerPort;
            }
        }
    }

    public static NatsServer StartJS() => StartJS(new NullOutputHelper(), TransportType.Tcp);

    public static NatsServer StartJS(ITestOutputHelper outputHelper, TransportType transportType) => Start(
        outputHelper: outputHelper,
        options: new NatsServerOptionsBuilder()
            .UseTransport(transportType)
            .UseJetStream()
            .Build());

    public static NatsServer Start() => Start(new NullOutputHelper(), TransportType.Tcp);

    public static NatsServer Start(ITestOutputHelper outputHelper) => Start(outputHelper, TransportType.Tcp);

    public static NatsServer Start(ITestOutputHelper outputHelper, TransportType transportType) =>
        Start(outputHelper, new NatsServerOptionsBuilder().UseTransport(transportType).Build());

    public static NatsServer Start(ITestOutputHelper outputHelper, NatsServerOptions options, NatsOpts? clientOptions = default)
    {
        NatsServer? server = null;
        NatsConnection? nats = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                server = new NatsServer(outputHelper, options);
                nats = server.CreateClientConnection(clientOptions ?? NatsOpts.Default, reTryCount: 3);
#pragma warning disable CA2012
                return server;
            }
            catch
            {
                server?.DisposeAsync();
            }
            finally
            {
                nats?.DisposeAsync();
#pragma warning restore CA2012
            }
        }

        throw new Exception("Can't start nats-server and connect to it");
    }

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

            if (Options.ServerDisposeReturnsPorts)
            {
                Options.Dispose();
            }
        }
    }

    public (NatsConnection, NatsProxy) CreateProxiedClientConnection(NatsOpts? options = null)
    {
        if (Options.EnableTls)
        {
            throw new Exception("Tapped mode doesn't work wit TLS");
        }

        var proxy = new NatsProxy(Options.ServerPort, _outputHelper, Options.Trace);

        var client = new NatsConnection((options ?? NatsOpts.Default) with
        {
            LoggerFactory = new OutputHelperLoggerFactory(_outputHelper),
            Url = $"nats://localhost:{proxy.Port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });

        return (client, proxy);
    }

    public NatsConnection CreateClientConnection(NatsOpts? options = default, int reTryCount = 10, bool ignoreAuthorizationException = false)
    {
        for (var i = 0; i < reTryCount; i++)
        {
            try
            {
                var nats = new NatsConnection(ClientOptions(options ?? NatsOpts.Default));

                try
                {
#pragma warning disable CA2012
                    nats.PingAsync().GetAwaiter().GetResult();
#pragma warning restore CA2012
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
        return new NatsConnectionPool(4, ClientOptions(opts));
    }

    public NatsOpts ClientOptions(NatsOpts opts)
    {
        return opts with
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
            TransportType = transportType,
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var opts2 = new NatsServerOptions
        {
            TransportType = transportType,
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var opts3 = new NatsServerOptions
        {
            TransportType = transportType,
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var routes = new[] { opts1, opts2, opts3 };
        foreach (var opt in routes)
        {
            opt.SetRoutes(routes);
        }

        Server1 = NatsServer.Start(outputHelper, opts1);
        Server2 = NatsServer.Start(outputHelper, opts2);
        Server3 = NatsServer.Start(outputHelper, opts3);
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

#pragma warning disable CS4014

public class NullOutputHelper : ITestOutputHelper
{
    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }
}
