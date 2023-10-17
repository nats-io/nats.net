namespace NATS.Client.ObjectStore;

/// <summary>
/// Object Store storage type
/// </summary>
public enum NatsObjStorageType
{
    File = 0,
    Memory = 1,
}

/// <summary>
/// Object store configuration.
/// </summary>
/// <param name="Bucket">Name of the bucket.</param>
public record NatsObjConfig(string Bucket)
{
    /// <summary>
    /// Bucket description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Maximum age of the object.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// How big the store may be, when the combined stream size exceeds this old keys are removed.
    /// </summary>
    public long? MaxBytes { get; init; }

    /// <summary>
    /// Type of backing storage to use.
    /// </summary>
    public NatsObjStorageType? Storage { get; init; }

    /// <summary>
    /// How many replicas to keep for each key.
    /// </summary>
    public int NumberOfReplicas { get; init; } = 1;

    /// <summary>
    /// Additional metadata for the bucket.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
