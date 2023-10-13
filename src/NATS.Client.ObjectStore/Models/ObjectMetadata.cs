using System.Text.Json.Serialization;

namespace NATS.Client.ObjectStore.Models;

public record ObjectMetadata
{
    /// <summary>
    /// Object name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Bucket name
    /// </summary>
    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = default!;

    /// <summary>
    /// Object NUID
    /// </summary>
    [JsonPropertyName("nuid")]
    public string Nuid { get; set; } = default!;

    /// <summary>
    /// Max chunk size
    /// </summary>
    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Size { get; set; } = default!;

    /// <summary>
    /// Modified timestamp
    /// </summary>
    [JsonPropertyName("mtime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset MTime { get; set; } = default!;

    /// <summary>
    /// Number of chunks
    /// </summary>
    [JsonPropertyName("chunks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Chunks { get; set; } = default!;

    /// <summary>
    /// Object digest
    /// </summary>
    [JsonPropertyName("digest")]
    public string Digest { get; set; } = default!;

    /// <summary>
    /// Object metadata
    /// </summary>
    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, string> Meta { get; set; } = default!;

    /// <summary>
    /// Object options
    /// </summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Options Options { get; set; } = default!;
}

public record Options
{
    /// <summary>
    /// Max chunk size
    /// </summary>
    [JsonPropertyName("max_chunk_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxChunkSize { get; set; } = default!;
}
