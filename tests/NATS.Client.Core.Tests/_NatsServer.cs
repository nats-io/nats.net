using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cysharp.Diagnostics;
using NATS.Client.Core;

namespace NATS.Client.Core.Tests;

public class NatsServer : IAsyncDisposable
{
    static readonly string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
    static readonly string natsServerPath = $"nats-server{ext}";

    readonly CancellationTokenSource cancellationTokenSource = new();
    readonly string? configFileName;
    readonly ITestOutputHelper outputHelper;
    readonly Task<string[]> processOut;
    readonly Task<string[]> processErr;
    readonly TransportType transportType;
    int disposed;

    public readonly NatsServerOptions Options;

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType)
        : this(outputHelper, transportType, transportType switch
        {
            TransportType.Tcp => new NatsServerOptions(),
            TransportType.Tls => new NatsServerOptions
            {
                EnableTls = true,
                TlsServerCertFile = "resources/certs/server-cert.pem",
                TlsServerKeyFile = "resources/certs/server-key.pem",
                TlsCaFile = "resources/certs/ca-cert.pem"
            },
            TransportType.WebSocket => new NatsServerOptions
            {
                EnableWebSocket = true
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transportType), transportType, null)
        })
    {
    }

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType, NatsServerOptions options)
    {
        this.outputHelper = outputHelper;
        this.transportType = transportType;
        Options = options;
        configFileName = Path.GetTempFileName();
        var config = options.ConfigFileContents;
        File.WriteAllText(configFileName, config);
        var cmd = $"{natsServerPath} -c {configFileName}";

        outputHelper.WriteLine("ProcessStart: " + cmd + Environment.NewLine + config);
        var (p, stdout, stderr) = ProcessX.GetDualAsyncEnumerable(cmd);

        processOut = EnumerateWithLogsAsync(stdout, cancellationTokenSource.Token);
        processErr = EnumerateWithLogsAsync(stderr, cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Options.ServerPort, cancellationTokenSource.Token);
                    if (client.Connected) return;
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(500, cancellationTokenSource.Token);
            }
        }).Wait(5000); // timeout

        if (processOut.IsFaulted)
        {
            processOut.GetAwaiter().GetResult(); // throw exception
        }

        if (processErr.IsFaulted)
        {
            processErr.GetAwaiter().GetResult(); // throw exception
        }

        outputHelper.WriteLine("OK to Process Start, Port:" + Options.ServerPort);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref disposed) != 1)
        {
            return;
        }

        cancellationTokenSource.Cancel(); // trigger of process kill.
        cancellationTokenSource.Dispose();
        try
        {
            var processLogs = await processErr; // wait for process exit, nats output info to stderror
            if (processLogs.Length != 0)
            {
                outputHelper.WriteLine("Process Logs of " + Options.ServerPort);
                foreach (var item in processLogs)
                {
                    outputHelper.WriteLine(item);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (configFileName != null)
            {
                File.Delete(configFileName);
            }

            if (Options.ServerDisposeReturnsPorts)
            {
                Options.Dispose();
            }
        }
    }

    async Task<string[]> EnumerateWithLogsAsync(ProcessAsyncEnumerable enumerable, CancellationToken cancellationToken)
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

    public string ClientUrl => transportType switch
    {
        TransportType.Tcp => $"nats://localhost:{Options.ServerPort}",
        TransportType.Tls => $"tls://localhost:{Options.ServerPort}",
        TransportType.WebSocket => $"ws://localhost:{Options.WebSocketPort}",
        _ => throw new ArgumentOutOfRangeException()
    };

    public NatsOptions ClientOptions(NatsOptions options)
    {
        return options with
        {
            LoggerFactory = new OutputHelperLoggerFactory(outputHelper),
            //ConnectTimeout = TimeSpan.FromSeconds(1),
            //ReconnectWait = TimeSpan.Zero,
            //ReconnectJitter = TimeSpan.Zero,
            TlsOptions = Options.EnableTls
                ? TlsOptions.Default with
                {
                    CertFile = Options.TlsClientCertFile,
                    KeyFile = Options.TlsClientKeyFile,
                    CaFile = Options.TlsCaFile
                }
                : TlsOptions.Default,
            Url = ClientUrl
        };
    }
}

public class NatsCluster : IAsyncDisposable
{
    public NatsServer Server1 { get; }
    public NatsServer Server2 { get; }
    public NatsServer Server3 { get; }

    public NatsCluster(ITestOutputHelper outputHelper, TransportType transportType)
    {
        var opts1 = new NatsServerOptions
        {
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true
        };
        var opts2 = new NatsServerOptions
        {
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true
        };
        var opts3 = new NatsServerOptions
        {
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true
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

    public async ValueTask DisposeAsync()
    {
        await Server1.DisposeAsync();
        await Server2.DisposeAsync();
        await Server3.DisposeAsync();
    }
}
