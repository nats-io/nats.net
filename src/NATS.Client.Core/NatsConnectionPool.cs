namespace NATS.Client.Core;

public sealed class NatsConnectionPool : IAsyncDisposable
{
    readonly NatsConnection[] connections;
    int index = -1;

    public NatsConnectionPool()
        : this(Environment.ProcessorCount / 2, NatsOptions.Default, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize)
        : this(poolSize, NatsOptions.Default, _ => { })
    {
    }

    public NatsConnectionPool(NatsOptions options)
        : this(Environment.ProcessorCount / 2, options, _ => { })
    {

    }

    public NatsConnectionPool(int poolSize, NatsOptions options)
        : this(poolSize, options, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize, NatsOptions options, Action<NatsConnection> configureConnection)
    {
        poolSize = Math.Max(1, poolSize);
        connections = new NatsConnection[poolSize];
        for (int i = 0; i < connections.Length; i++)
        {
            var name = (options.Name == null) ? $"#{i}" : $"{options.Name}#{i}";
            var conn = new NatsConnection(options with {  Name = name });
            configureConnection(conn);
            connections[i] = conn;
        }
    }

    public NatsConnection GetConnection()
    {
        var i = Interlocked.Increment(ref index);
        return connections[i % connections.Length];
    }

    public IEnumerable<NatsConnection> GetConnections()
    {
        foreach (var item in connections)
        {
            yield return item;
        }
    }

    public INatsCommand GetCommand()
    {
        return GetConnection();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in connections)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }
}
