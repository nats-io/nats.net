using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NATS.Client.Services;

public record NatsSvcConfig
{
    private static readonly Regex NameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.Compiled);

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

    public string Name { get; }

    public string Version { get; }

    public string? Description { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }

    public string QueueGroup { get; init; } = "q";

    public Func<JsonNode>? StatsHandler { get; init; }
}
