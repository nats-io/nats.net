using Synadia.Orbit.Testing.NatsServerProcessManager;

public sealed class NatsServerFixture : IDisposable
{
    public NatsServerFixture()
    {
        Server = NatsServerProcess.Start();
    }

    public NatsServerProcess Server { get; }

    public void Dispose() => Server.Dispose();
}

[CollectionDefinition("nats-server")]
public class NatsServerCollection : ICollectionFixture<NatsServerFixture>
{
}
