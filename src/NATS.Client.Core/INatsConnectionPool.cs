namespace NATS.Client.Core;

public interface INatsConnectionPool : IAsyncDisposable
{
    INatsConnection GetConnection();

    IEnumerable<INatsConnection> GetConnections();
}
