using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core2.Tests;

// https://xunit.net/docs/shared-context#collection-fixture
public class NatsServerFixture : IDisposable
{
    private int _next;

    public NatsServerFixture()
        : this(null)
    {
    }

    protected NatsServerFixture(string? config)
    {
        Server = NatsServerProcess.Start(config: config);
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
public class NatsServerCollection : ICollectionFixture<NatsServerFixture>
{
}

public class NatsServerRestrictedUserFixture() : NatsServerFixture("resources/configs/restricted-user.conf");

[CollectionDefinition("nats-server-restricted-user")]
public class NatsServerRestrictedUserCollection : ICollectionFixture<NatsServerRestrictedUserFixture>
{
}
