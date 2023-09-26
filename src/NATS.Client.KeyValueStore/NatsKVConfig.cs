﻿namespace NATS.Client.KeyValueStore;

public record NatsKVConfig
{
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

    // Bucket mirror configuration.
    // pub mirror: Option<Source>,
    // Bucket sources configuration.
    // pub sources: Option<Vec<Source>>,
    // Allow mirrors using direct API.
    // pub mirror_direct: bool,
}

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
