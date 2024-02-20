namespace NATS.Client.JetStream.Models;

/// <summary>
/// Placement requirements for a stream
/// </summary>

public record Placement
{
    /// <summary>
    /// The desired cluster name to place the stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("cluster")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Cluster { get; set; }

    /// <summary>
    /// Tags required on servers hosting this stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? Tags { get; set; }
}
