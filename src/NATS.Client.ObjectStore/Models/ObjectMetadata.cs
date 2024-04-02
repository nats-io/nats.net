using System.Text.Json.Serialization;

namespace NATS.Client.ObjectStore.Models;

public record ObjectMetadata
{
    /// <summary>
    /// Object name
    /// </summary>
    [JsonPropertyName("name")]
#if NET6_0
    public string Name { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Name { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// Object description
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Bucket name
    /// </summary>
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    /// <summary>
    /// Object NUID
    /// </summary>
    [JsonPropertyName("nuid")]
    public string? Nuid { get; set; }

    /// <summary>
    /// Max chunk size
    /// </summary>
    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Size { get; set; }

    /// <summary>
    /// Modified timestamp
    /// </summary>
    [JsonPropertyName("mtime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset MTime { get; set; }

    /// <summary>
    /// Number of chunks
    /// </summary>
    [JsonPropertyName("chunks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Chunks { get; set; }

    /// <summary>
    /// Object digest
    /// </summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }

    /// <summary>
    /// Object metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Object metadata
    /// </summary>
    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, string[]>? Headers { get; set; }

    /// <summary>
    /// Object deleted
    /// </summary>
    [JsonPropertyName("deleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Deleted { get; set; }

    /// <summary>
    /// Object options
    /// </summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MetaDataOptions? Options { get; set; }
}

public record MetaDataOptions
{
    /// <summary>
    /// Link
    /// </summary>
    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NatsObjLink? Link { get; set; }

    /// <summary>
    /// Max chunk size
    /// </summary>
    [JsonPropertyName("max_chunk_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? MaxChunkSize { get; set; }
}

public record NatsObjLink
{
    /// <summary>
    /// Link name
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
#if NET6_0
    public string Name { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Name { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// Bucket name
    /// </summary>
    [JsonPropertyName("bucket")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
#if NET6_0
    public string Bucket { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Bucket { get; set; }
#pragma warning restore SA1206
#endif
}
