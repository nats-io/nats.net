using NATS.Client.Platform.Windows.Tests;

namespace NATS.Client.Core2.Tests;

// https://xunit.net/docs/shared-context#collection-fixture
public class NatsServerFixture : IDisposable
{
    private int _next;

    public NatsServerFixture()
    {
        Server = NatsServerProcess.Start();
    }

    public NatsServerProcess Server { get; }

    public int Port => new Uri(Server.Url).Port;

    public string Url => Server.Url;

    public string GetNextId() => $"test{Interlocked.Increment(ref _next)}";

    public void Dispose()
    {
        Server.Dispose();
    }
}

[CollectionDefinition("nats-server")]
public class DatabaseCollection : ICollectionFixture<NatsServerFixture>
{
}
