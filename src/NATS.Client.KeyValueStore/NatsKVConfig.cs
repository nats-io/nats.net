using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Key Value Store configuration
/// </summary>
public record NatsKVConfig
{
    /// <summary>
    /// Create a new configuration
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    public NatsKVConfig(string bucket) => Bucket = bucket;

    /// <summary>
    /// Name of the bucket
    /// </summary>
    public string Bucket { get; init; }

    /// <summary>
    /// Human readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Maximum size of a single value.
    /// </summary>
    public int MaxValueSize { get; init; }

    /// <summary>
    /// Maximum historical entries.
    /// </summary>
    public long History { get; init; }

    /// <summary>
    /// Maximum age of any entry in the bucket, expressed in nanoseconds
    /// </summary>
    public TimeSpan MaxAge { get; init; }

    /// <summary>
    /// How large the bucket may become in total bytes before the configured discard policy kicks in
    /// </summary>
    public long MaxBytes { get; init; }

    /// <summary>
    /// The type of storage backend, `File` (default) and `Memory`
    /// </summary>
    public NatsKVStorageType Storage { get; init; }

    /// <summary>
    /// How many replicas to keep for each entry in a cluster.
    /// </summary>
    public int NumberOfReplicas { get; init; }

    /// <summary>
    /// Republish is for republishing messages once persistent in the Key Value Bucket.
    /// </summary>
    public NatsKVRepublish? Republish { get; init; }

    /// <summary>
    /// Placement requirements for a key-value store stream.
    /// </summary>
    public Placement? Placement { get; init; }

    /// <summary>
    /// Use compressed storage.
    /// </summary>
    public bool Compression { get; init; }

    /// <summary>
    /// Mirror defines the configuration for mirroring another KeyValue store
    /// </summary>
    public StreamSource? Mirror { get; init; }

    /// <summary>
    /// Sources defines the configuration for sources of a KeyValue store.
    /// </summary>
    public ICollection<StreamSource>? Sources { get; set; }

    /// <summary>
    /// Additional metadata for the Bucket
    /// </summary>
    public IDictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// How long the bucket keeps markers when keys are removed by the TTL setting, 0 meaning markers are not supported.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    public TimeSpan LimitMarkerTTL { get; init; }
}

/// <summary>
/// Key Value Store republish configuration
/// </summary>
public record NatsKVRepublish
{
    /// <summary>
    /// Subject that should be republished.
    /// </summary>
    public string? Src { get; init; }

    /// <summary>
    /// Subject where messages will be republished.
    /// </summary>
    public string? Dest { get; init; }

    /// <summary>
    /// If true, only headers should be republished.
    /// </summary>
    public bool HeadersOnly { get; init; }
}
