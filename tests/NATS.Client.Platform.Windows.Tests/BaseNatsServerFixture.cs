using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Platform.Windows.Tests;

public class BaseNatsServerFixture : IDisposable
{
    private readonly NatsServerProcess _server;
    private int _next;

    protected BaseNatsServerFixture(string? config = default) => _server = NatsServerProcess.Start(config: config);

    public string Url => _server.Url;

    public string GetNextId() => $"test{Interlocked.Increment(ref _next)}";

    public void Dispose() => _server.Dispose();
}
