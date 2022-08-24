using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cysharp.Diagnostics;

namespace AlterNats.Tests;

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
    bool isDisposed;

    public readonly NatsServerPorts Ports;

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType, string argument = "")
        : this(outputHelper, transportType, new NatsServerPorts(new NatsServerPortOptions
        {
            WebSocket = transportType == TransportType.WebSocket
        }), argument)
    {
    }

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType, NatsServerPorts ports,
        string argument = "")
    {
        this.outputHelper = outputHelper;
        this.transportType = transportType;
        Ports = ports;
        var cmd = $"{natsServerPath} -p {Ports.ServerPort} {argument}".Trim();

        if (transportType == TransportType.WebSocket)
        {
            configFileName = Path.GetTempFileName();
            var contents = "";
            contents += "websocket {" + Environment.NewLine;
            contents += $"  port: {Ports.WebSocketPort}" + Environment.NewLine;
            contents += "  no_tls: true" + Environment.NewLine;
            contents += "}" + Environment.NewLine;
            File.WriteAllText(configFileName, contents);
            cmd = $"{cmd} -c {configFileName}";
        }

        outputHelper.WriteLine("ProcessStart: " + cmd);
        var (p, stdout, stderror) = ProcessX.GetDualAsyncEnumerable(cmd);

        processOut = EnumerateWithLogsAsync(stdout, cancellationTokenSource.Token);
        processErr = EnumerateWithLogsAsync(stderror, cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Ports.ServerPort, cancellationTokenSource.Token);
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

        outputHelper.WriteLine("OK to Process Start, Port:" + Ports.ServerPort);
    }

    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            cancellationTokenSource.Cancel(); // trigger of process kill.
            cancellationTokenSource.Dispose();
            try
            {
                var processLogs = await processErr; // wait for process exit, nats output info to stderror
                if (processLogs.Length != 0)
                {
                    outputHelper.WriteLine("Process Logs of " + Ports.ServerPort);
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

                if (Ports.ServerDisposeReturnsPorts)
                {
                    Ports.Dispose();
                }
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
        TransportType.Tcp => $"localhost:{Ports.ServerPort}",
        TransportType.WebSocket => $"ws://localhost:{Ports.WebSocketPort}",
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
        var port1 = new NatsServerPorts(new NatsServerPortOptions
        {
            WebSocket = transportType == TransportType.WebSocket,
            Clustering = true
        });
        var port2 = new NatsServerPorts(new NatsServerPortOptions
        {
            WebSocket = transportType == TransportType.WebSocket,
            Clustering = true
        });
        var port3 = new NatsServerPorts(new NatsServerPortOptions
        {
            WebSocket = transportType == TransportType.WebSocket,
            Clustering = true
        });

        var baseArgument =
            $"--cluster_name test-cluster -routes nats://localhost:{port1.ClusteringPort},nats://localhost:{port2.ClusteringPort},nats://localhost:{port3.ClusteringPort}";

        Server1 = new NatsServer(outputHelper, transportType, port1,
            $"{baseArgument} -cluster nats://localhost:{port1.ClusteringPort}");
        Server2 = new NatsServer(outputHelper, transportType, port2,
            $"{baseArgument} -cluster nats://localhost:{port2.ClusteringPort}");
        Server3 = new NatsServer(outputHelper, transportType, port3,
            $"{baseArgument} -cluster nats://localhost:{port3.ClusteringPort}");
    }

    public async ValueTask DisposeAsync()
    {
        await Server1.DisposeAsync();
        await Server2.DisposeAsync();
        await Server3.DisposeAsync();
    }
}
