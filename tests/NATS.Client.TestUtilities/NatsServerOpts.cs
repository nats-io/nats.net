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

public sealed class NatsServerOptsBuilder
{
    private readonly List<string> _extraConfigs = new();
    private bool _enableWebSocket;
    private bool _enableTls;
    private bool _tlsFirst;
    private bool _tlsVerify;
    private bool _enableJetStream;
    private string? _serverName;
    private string? _tlsServerCertFile;
    private string? _tlsServerKeyFile;
    private string? _tlsClientCertFile;
    private string? _tlsClientKeyFile;
    private string? _tlsCaFile;
    private TransportType? _transportType;
    private bool _serverDisposeReturnsPorts;
    private bool _enableClustering;
    private bool _trace;

    public NatsServerOpts Build() => new()
    {
        EnableWebSocket = _enableWebSocket,
        EnableTls = _enableTls,
        TlsFirst = _tlsFirst,
        TlsVerify = _tlsVerify,
        EnableJetStream = _enableJetStream,
        ServerName = _serverName,
        TlsServerCertFile = _tlsServerCertFile,
        TlsServerKeyFile = _tlsServerKeyFile,
        TlsClientCertFile = _tlsClientCertFile,
        TlsClientKeyFile = _tlsClientKeyFile,
        TlsCaFile = _tlsCaFile,
        ExtraConfigs = _extraConfigs,
        TransportType = _transportType ?? TransportType.Tcp,
        ServerDisposeReturnsPorts = _serverDisposeReturnsPorts,
        EnableClustering = _enableClustering,
        Trace = _trace,
    };

    public NatsServerOptsBuilder EnableClustering()
    {
        _enableClustering = true;
        return this;
    }

    public NatsServerOptsBuilder WithServerDisposeReturnsPorts()
    {
        _serverDisposeReturnsPorts = true;
        return this;
    }

    public NatsServerOptsBuilder Trace()
    {
        _trace = true;
        return this;
    }

    public NatsServerOptsBuilder UseTransport(TransportType transportType, bool tlsFirst = false, bool tlsVerify = false)
    {
        _transportType = transportType;

        if (transportType != TransportType.Tls && tlsFirst)
        {
            throw new Exception("tlsFirst is only valid for TLS transport");
        }

        if (transportType == TransportType.Tls)
        {
            _enableTls = true;
            _tlsServerCertFile = "resources/certs/server-cert.pem";
            _tlsServerKeyFile = "resources/certs/server-key.pem";

            if (tlsVerify)
            {
                _tlsClientCertFile = "resources/certs/client-cert.pem";
                _tlsClientKeyFile = "resources/certs/client-key.pem";
            }

            _tlsCaFile = "resources/certs/ca-cert.pem";
            _tlsFirst = tlsFirst;
            _tlsVerify = tlsVerify;
        }
        else if (transportType == TransportType.WebSocket)
        {
            _enableWebSocket = true;
        }

        return this;
    }

    public NatsServerOptsBuilder WithServerName(string serverName)
    {
        _serverName = serverName;
        return this;
    }

    public NatsServerOptsBuilder UseJetStream()
    {
        _enableJetStream = true;
        return this;
    }

    public NatsServerOptsBuilder AddServerConfig(string config)
    {
        _extraConfigs.Add(File.ReadAllText(config));
        return this;
    }

    public NatsServerOptsBuilder AddServerConfigText(string configText)
    {
        _extraConfigs.Add(configText);
        return this;
    }
}

public sealed class NatsServerOpts : IDisposable
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

    public NatsServerOpts()
    {
        _lazyServerPort = new Lazy<int>(LeasePort);
        _lazyClusteringPort = new Lazy<int?>(() => EnableClustering ? LeasePort() : null);
        _lazyWebSocketPort = new Lazy<int?>(() => EnableWebSocket ? LeasePort() : null);
    }

    public bool EnableClustering { get; init; }

    public bool EnableWebSocket { get; init; }

    public bool EnableTls { get; init; }

    public bool EnableJetStream { get; init; }

    public string? ServerName { get; init; }

    public string? ServerHost { get; init; } = "127.0.0.1";

    public string? JetStreamStoreDir { get; set; }

    public bool ServerDisposeReturnsPorts { get; init; } = true;

    public string? TlsClientCertFile { get; init; }

    public string? TlsClientKeyFile { get; init; }

    public string? TlsServerCertFile { get; init; }

    public string? TlsServerKeyFile { get; init; }

    public string? TlsCaFile { get; init; }

    public bool TlsFirst { get; init; } = false;

    public bool TlsVerify { get; init; } = false;

    public TransportType TransportType { get; init; }

    public bool Trace { get; init; }

    public List<string> ExtraConfigs { get; init; } = new();

    public int ServerPort => _lazyServerPort.Value;

    public int? ClusteringPort => _lazyClusteringPort.Value;

    public int? WebSocketPort => _lazyWebSocketPort.Value;

    public string ConfigFileContents
    {
        get
        {
            var sb = new StringBuilder();

            if (ServerName != null)
            {
                sb.AppendLine($"server_name: {ServerName}");
            }

            sb.AppendLine($"listen: {ServerHost}:{ServerPort}");

            if (Trace)
            {
                sb.AppendLine($"trace: true");
                sb.AppendLine($"debug: true");
            }

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
                sb.AppendLine($"  listen: {ServerHost}:{ClusteringPort}");
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

                if (TlsFirst)
                {
                    sb.AppendLine($"  handshake_first: true");
                }

                if (TlsVerify)
                {
                    sb.AppendLine($"  verify_and_map: true");
                }

                sb.AppendLine("}");
            }

            if (EnableJetStream)
            {
                sb.AppendLine("jetstream {");
                sb.AppendLine($"  store_dir: '{JetStreamStoreDir}'");
                sb.AppendLine("}");
            }

            foreach (var config in ExtraConfigs)
                sb.AppendLine(config);

            return sb.ToString();
        }
    }

    public void SetRoutes(IEnumerable<NatsServerOpts> options)
    {
        _routes = string.Join(",", options.Select(o => $"nats://127.0.0.1:{o.ClusteringPort}"));
    }

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
