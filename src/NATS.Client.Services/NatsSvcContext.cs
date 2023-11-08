using NATS.Client.Core;

namespace NATS.Client.Services;

/// <summary>
/// NATS service context.
/// </summary>
public class NatsSvcContext : INatsSvcContext
{
    private readonly NatsConnection _nats;

    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcContext"/>.
    /// </summary>
    /// <param name="nats">NATS connection.</param>
    public NatsSvcContext(NatsConnection nats) => _nats = nats;

    /// <summary>
    /// Adds a new service.
    /// </summary>
    /// <param name="name">Service name.</param>
    /// <param name="version">Service SemVer version.</param>
    /// <param name="queueGroup">Optional queue group (default: "q")</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>NATS Service instance.</returns>
    public ValueTask<INatsSvcServer> AddServiceAsync(string name, string version, string queueGroup = "q", CancellationToken cancellationToken = default) =>
        AddServiceAsync(new NatsSvcConfig(name, version) { QueueGroup = queueGroup }, cancellationToken);

    /// <summary>
    /// Adds a new service.
    /// </summary>
    /// <param name="config">Service configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>NATS Service instance.</returns>
    public async ValueTask<INatsSvcServer> AddServiceAsync(NatsSvcConfig config, CancellationToken cancellationToken = default)
    {
        var service = new NatsSvcServer(_nats, config, cancellationToken);
        await service.StartAsync().ConfigureAwait(false);
        return service;
    }
}
