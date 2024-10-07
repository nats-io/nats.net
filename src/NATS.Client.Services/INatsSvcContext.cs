using NATS.Client.Core;

namespace NATS.Client.Services;

/// <summary>
/// NATS Services context.
/// </summary>
public interface INatsSvcContext
{
    /// <summary>
    /// Gets the associated NATS connection.
    /// </summary>
    INatsConnection Connection { get; }

    /// <summary>
    /// Adds a new service.
    /// </summary>
    /// <param name="name">Service name.</param>
    /// <param name="version">Service SemVer version.</param>
    /// <param name="queueGroup">Optional queue group (default: "q")</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>NATS Service instance.</returns>
    ValueTask<INatsSvcServer> AddServiceAsync(string name, string version, string queueGroup = "q", CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new service.
    /// </summary>
    /// <param name="config">Service configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>NATS Service instance.</returns>
    ValueTask<INatsSvcServer> AddServiceAsync(NatsSvcConfig config, CancellationToken cancellationToken = default);
}
