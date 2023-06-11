using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Text;

namespace NATS.Client.Core.Tests;

public enum TransportType
{
    Tcp,
    Tls,
    WebSocket,
}

public sealed class NatsServerOptionsBuilder
{
    private readonly List<string> _extraConfigs = new();
    private bool _enableWebSocket;
    private bool _enableTls;
    private string? _tlsServerCertFile;
    private string? _tlsServerKeyFile;
    private string? _tlsCaFile;

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
    private static readonly Lazy<ConcurrentQueue<int>> PortFactory = new(() =>
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

    private readonly Lazy<int> _lazyServerPort;

    private readonly Lazy<int?> _lazyClusteringPort;

    private readonly Lazy<int?> _lazyWebSocketPort;

    private int _disposed;
    private string _routes = string.Empty;
    private int _ephemeralTcpPort = -1;
    private int _ephemeralWsPort = -1;

    public NatsServerOptions()
    {
        _lazyServerPort = new Lazy<int>(LeasePort);
        _lazyClusteringPort = new Lazy<int?>(() => EnableClustering ? LeasePort() : null);
        _lazyWebSocketPort = new Lazy<int?>(() => EnableWebSocket ? LeasePort() : null);
    }

    public bool EnableClustering { get; init; }

    public bool EnableWebSocket { get; init; }

    public bool EnableTls { get; init; }

    public bool ServerDisposeReturnsPorts { get; init; } = true;

    public string? TlsClientCertFile { get; init; }

    public string? TlsClientKeyFile { get; init; }

    public string? TlsServerCertFile { get; init; }

    public string? TlsServerKeyFile { get; init; }

    public string? TlsCaFile { get; init; }

    // Use ephemeral ports where possible to reduce intermittent test failures.
    public bool UseEphemeralPort { get; init; } = true;

    public List<string> ExtraConfigs { get; init; } = new();

    public int ServerPort => UseEphemeralPort ? Volatile.Read(ref _ephemeralTcpPort) : _lazyServerPort.Value;

    public int? ClusteringPort => _lazyClusteringPort.Value;

    public int? WebSocketPort => UseEphemeralPort ? Volatile.Read(ref _ephemeralWsPort) : _lazyWebSocketPort.Value;

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
                sb.AppendLine($"  routes: [{_routes}]");
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

    public void SetRoutes(IEnumerable<NatsServerOptions> options)
    {
        _routes = string.Join(",", options.Select(o => $"nats://localhost:{o.ClusteringPort}"));
    }

    public void SetEphemeralTcpPort(int port) => Interlocked.Exchange(ref _ephemeralTcpPort, port);

    public void SetEphemeralWsPort(int port) => Interlocked.Exchange(ref _ephemeralWsPort, port);

    public void Dispose()
    {
        if (Interlocked.Increment(ref _disposed) != 1)
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

    private static int LeasePort()
    {
        if (PortFactory.Value.TryDequeue(out var port))
        {
            return port;
        }

        throw new Exception("unable to allocate port");
    }

    private static void ReturnPort(int port)
    {
        PortFactory.Value.Enqueue(port);
    }
}
