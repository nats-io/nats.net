using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cysharp.Diagnostics;

namespace AlterNats.Tests;

public class NatsServer : IAsyncDisposable
{
    static readonly string Ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
    static readonly string NatsServerPath = $"nats-server{Ext}";

    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly string? _configFileName;
    readonly ITestOutputHelper _outputHelper;
    readonly Task<string[]> _processOut;
    readonly Task<string[]> _processErr;
    readonly TransportType _transportType;
    bool _isDisposed;

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
        _outputHelper = outputHelper;
        _transportType = transportType;
        Ports = ports;
        var cmd = $"{NatsServerPath} -p {Ports.ServerPort} {argument}".Trim();

        if (transportType == TransportType.WebSocket)
        {
            _configFileName = Path.GetTempFileName();
            var contents = "";
            contents += "websocket {" + Environment.NewLine;
            contents += $"  port: {Ports.WebSocketPort}" + Environment.NewLine;
            contents += "  no_tls: true" + Environment.NewLine;
            contents += "}" + Environment.NewLine;
            File.WriteAllText(_configFileName, contents);
            cmd = $"{cmd} -c {_configFileName}";
        }

        outputHelper.WriteLine("ProcessStart: " + cmd);
        var (p, stdout, stderror) = ProcessX.GetDualAsyncEnumerable(cmd);

        _processOut = EnumerateWithLogsAsync(stdout, _cancellationTokenSource.Token);
        _processErr = EnumerateWithLogsAsync(stderror, _cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Ports.ServerPort, _cancellationTokenSource.Token);
                    if (client.Connected) return;
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

        outputHelper.WriteLine("OK to Process Start, Port:" + Ports.ServerPort);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _cancellationTokenSource.Cancel(); // trigger of process kill.
            _cancellationTokenSource.Dispose();
            try
            {
                var processLogs = await _processErr; // wait for process exit, nats output info to stderror
                if (processLogs.Length != 0)
                {
                    _outputHelper.WriteLine("Process Logs of " + Ports.ServerPort);
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

    public string ClientUrl => _transportType switch
    {
        TransportType.Tcp => $"localhost:{Ports.ServerPort}",
        TransportType.WebSocket => $"ws://localhost:{Ports.WebSocketPort}",
        _ => throw new ArgumentOutOfRangeException()
    };

    public NatsOptions ClientOptions(NatsOptions options)
    {
        return options with
        {
            LoggerFactory = new OutputHelperLoggerFactory(_outputHelper),
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
