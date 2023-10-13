namespace NATS.Client.ObjectStore;

/// <summary>
/// Object Store storage type
/// </summary>
public enum NatsOBStorageType
{
    File = 0,
    Memory = 1,
}

public record NatsOBConfig(string Bucket)
{
    public string? Description { get; init; }

    public TimeSpan? MaxAge { get; init; }

    public long? MaxBytes { get; init; }

    public NatsOBStorageType? Storage { get; init; }

    public int NumberOfReplicas { get; init; } = 1;

    public Dictionary<string, string>? Metadata { get; init; }
}
