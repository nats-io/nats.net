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
    /// Queue group name. (default: "q")
    /// </summary>
    public string QueueGroup { get; init; } = "q";

    /// <summary>
    /// Stats handler. JSON object returned by this handler will be included in
    /// the service stats <c>data</c> property.
    /// </summary>
    public Func<INatsSvcEndpoint, JsonNode>? StatsHandler { get; init; }
}

