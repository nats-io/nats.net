using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace AlterNats.Tests;

public class NatsServerPorts : IDisposable
{
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

    public readonly int ServerPort;
    public readonly int? ClusteringPort;
    public readonly int? WebSocketPort;
    public readonly bool ServerDisposeReturnsPorts;

    bool _isDisposed;

    public NatsServerPorts() : this(new NatsServerPortOptions())
    {
    }

    public NatsServerPorts(NatsServerPortOptions portOptions)
    {
        ServerPort = LeasePort();
        ServerDisposeReturnsPorts = portOptions.ServerDisposeReturnsPorts;
        if (portOptions.Clustering)
        {
            ClusteringPort = LeasePort();
        }

        if (portOptions.WebSocket)
        {
            WebSocketPort = LeasePort();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
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

public sealed record NatsServerPortOptions
{
    public bool Clustering { get; init; } = false;

    public bool WebSocket { get; init; } = false;

    public bool ServerDisposeReturnsPorts { get; init; } = true;
}

public enum TransportType
{
    Tcp,
    WebSocket
}
