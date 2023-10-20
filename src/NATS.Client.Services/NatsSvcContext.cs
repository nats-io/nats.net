using NATS.Client.Core;

namespace NATS.Client.Services;

public class NatsSvcContext
{
    private readonly NatsConnection _nats;

    public NatsSvcContext(NatsConnection nats) => _nats = nats;

    public ValueTask<NatsSvcService> AddServiceAsync(string name, string version, string queueGroup = "q", CancellationToken cancellationToken = default) =>
        AddServiceAsync(new NatsSvcConfig(name, version) { QueueGroup = queueGroup }, cancellationToken);

    public async ValueTask<NatsSvcService> AddServiceAsync(NatsSvcConfig config, CancellationToken cancellationToken = default)
    {
        var service = new NatsSvcService(_nats, config, cancellationToken);
        await service.StartAsync().ConfigureAwait(false);
        return service;
    }
}
