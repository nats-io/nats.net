using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NATS.Client.Services;

/// <summary>
/// NATS service configuration.
/// </summary>
public record NatsSvcConfig
{
    private static readonly Regex NameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.Compiled);

    private readonly int _endpointConcurrency = 1;

    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcConfig"/>.
    /// </summary>
    /// <param name="name">Service name.</param>
    /// <param name="version">Service SemVer version.</param>
    /// <exception cref="ArgumentException">Name or version is invalid.</exception>
    public NatsSvcConfig(string name, string version)
    {
        if (!NameRegex.IsMatch(name))
        {
            throw new ArgumentException("Invalid service name (name can only have A-Z, a-z, 0-9, dash and underscore).", nameof(name));
        }

        if (!VersionRegex.IsMatch(version))
        {
            throw new ArgumentException("Invalid service version (must use Semantic Versioning).", nameof(version));
        }

        Name = name;
        Version = version;
    }

    /// <summary>
    /// Service name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Service version. Must be a valid Semantic Versioning string.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Service description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Service metadata. This will be included in the service info.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Queue group name. (default: "q" unless <see cref="UseQueueGroup"/> is set to <c>false</c>)
    /// </summary>
    public string QueueGroup { get; init; } = "q";

    /// <summary>
    /// Use a queue group when creating service subscriptions. (default: <c>true</c>)
    /// Queue group name is defined using <see cref="QueueGroup"/> property.
    /// </summary>
    public bool UseQueueGroup { get; init; } = true;

    /// <summary>
    /// Stats handler. JSON object returned by this handler will be included in
    /// the service stats <c>data</c> property.
    /// </summary>
    public Func<INatsSvcEndpoint, JsonNode>? StatsHandler { get; init; }

    /// <summary>
    /// Maximum number of requests each endpoint of the service handles at the same time. (default: <c>1</c>)
    /// </summary>
    /// <remarks>
    /// By default an endpoint handles its requests one at a time, in the order they are received.
    /// When set to a value greater than <c>1</c>, each endpoint runs up to that many handlers
    /// concurrently, so handlers must be safe to run in parallel and requests may complete
    /// in a different order than they were received. Endpoint stats still count every request,
    /// and stopping the service or removing an endpoint still waits for all handlers in flight.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than <c>1</c>.</exception>
    public int EndpointConcurrency
    {
        get => _endpointConcurrency;
        init
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(EndpointConcurrency), value, "Endpoint concurrency must be at least 1.");
            }

            _endpointConcurrency = value;
        }
    }
}
