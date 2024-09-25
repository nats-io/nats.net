using NATS.Client.Platform.Windows.Tests;

namespace NATS.Client.Core2.Tests;

// https://xunit.net/docs/shared-context#collection-fixture
public class NatsServerFixture : IDisposable
{
    public NatsServerFixture()
    {
        Server = NatsServerProcess.Start();
    }

    public NatsServerProcess Server { get; }

    public string Url => Server.Url;

    public void Dispose()
    {
        Server.Dispose();
    }
}

[CollectionDefinition("nats-server")]
public class DatabaseCollection : ICollectionFixture<NatsServerFixture>
{
}