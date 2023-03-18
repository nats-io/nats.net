using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Text;

namespace NATS.Client.Core.Tests;

public sealed class NatsServerOptionsBuilder
{
    readonly List<string> _extraConfigs = new();
    bool _enableWebSocket;
    bool _enableTls;
    string? _tlsServerCertFile;
    string? _tlsServerKeyFile;
    string? _tlsCaFile;
    public NatsServerOptions Build()
    {
        return new NatsServerOptions
        {
            EnableWebSocket = _enableWebSocket,
            EnableTls = _enableTls,
            TlsServerCertFile = _tlsServerCertFile,
            TlsServerKeyFile = _tlsServerKeyFile,
            TlsCaFile = _tlsCaFile,
            ExtraConfigs = _extraConfigs,
        };
    }

    public NatsServerOptionsBuilder UseTransport(TransportType transportType)
    {
        if (transportType == TransportType.Tls)
        {
            _enableTls = true;
            _tlsServerCertFile = "resources/certs/server-cert.pem";
            _tlsServerKeyFile = "resources/certs/server-key.pem";
            _tlsCaFile = "resources/certs/ca-cert.pem";
        }
        else if (transportType == TransportType.WebSocket)
        {
            _enableWebSocket = true;
        }
        return this;
    }

    public NatsServerOptionsBuilder AddServerConfig(string config)
    {
        _extraConfigs.Add(File.ReadAllText(config));
        return this;
    }
}

public sealed class NatsServerOptions : IDisposable
{
    public bool EnableClustering { get; init; }
    public bool EnableWebSocket { get; init; }
    public bool EnableTls { get; init; }
    public bool ServerDisposeReturnsPorts { get; init; } = true;
    public string? TlsClientCertFile { get; init; }
    public string? TlsClientKeyFile { get; init; }
    public string? TlsServerCertFile { get; init; }
    public string? TlsServerKeyFile { get; init; }
    public string? TlsCaFile { get; init; }
    public List<string> ExtraConfigs { get; init; } = new();

    int disposed;
    string routes = "";
    readonly Lazy<int> lazyServerPort;
    readonly Lazy<int?> lazyClusteringPort;
    readonly Lazy<int?> lazyWebSocketPort;

    static readonly Lazy<ConcurrentQueue<int>> portFactory = new(() =>
    {
        const int start = 1024;
        const int size = 4096;
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var activePorts = new HashSet<int>(properties.GetActiveTcpListeners()
            .Where(m => m.Port is >= start and < start + size)
            .Select(m => m.Port));
        var freePorts = new HashSet<int>(Enumerable.Range(start, size));
        freePorts.ExceptWith(activePorts);
        return new ConcurrentQueue<int>(freePorts);
    });

    static int LeasePort()
    {
        if (portFactory.Value.TryDequeue(out var port))
        {
            return port;
        }

        throw new Exception("unable to allocate port");
    }

    static void ReturnPort(int port)
    {
        portFactory.Value.Enqueue(port);
    }

    public NatsServerOptions()
    {
        lazyServerPort = new Lazy<int>(LeasePort);
        lazyClusteringPort = new Lazy<int?>(() => EnableClustering ? LeasePort() : null);
        lazyWebSocketPort = new Lazy<int?>(() => EnableWebSocket ? LeasePort() : null);
    }

    public int ServerPort => lazyServerPort.Value;
    public int? ClusteringPort => lazyClusteringPort.Value;
    public int? WebSocketPort => lazyWebSocketPort.Value;

    public void SetRoutes(IEnumerable<NatsServerOptions> options)
    {
        routes = string.Join(",", options.Select(o => $"nats://localhost:{o.ClusteringPort}"));
    }

    public string ConfigFileContents
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"port: {ServerPort}");
            if (EnableWebSocket)
            {
                sb.AppendLine("websocket {");
                sb.AppendLine($"  port: {WebSocketPort}");
                sb.AppendLine("  no_tls: true");
                sb.AppendLine("}");
            }

            if (EnableClustering)
            {
                sb.AppendLine("cluster {");
                sb.AppendLine("  name: nats");
                sb.AppendLine($"  port: {ClusteringPort}");
                sb.AppendLine($"  routes: [{routes}]");
                sb.AppendLine("}");
            }

            if (EnableTls)
            {
                if (TlsServerCertFile == default || TlsServerKeyFile == default)
                {
                    throw new Exception("TLS is enabled but cert or key missing");
                }
                sb.AppendLine("tls {");
                sb.AppendLine($"  cert_file: {TlsServerCertFile}");
                sb.AppendLine($"  key_file: {TlsServerKeyFile}");
                if (TlsCaFile != default)
                {
                    sb.AppendLine($"  ca_file: {TlsCaFile}");
                }
                sb.AppendLine("}");
            }

            foreach (var config in ExtraConfigs)
                sb.AppendLine(config);

            return sb.ToString();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Increment(ref disposed) != 1)
        {
            return;
        }

        ReturnPort(ServerPort);
        if (ClusteringPort.HasValue)
        {
            ReturnPort(ClusteringPort.Value);
        }

        if (WebSocketPort.HasValue)
        {
            ReturnPort(WebSocketPort.Value);
        }
    }
}

public enum TransportType
{
    Tcp,
    Tls,
    WebSocket
}
