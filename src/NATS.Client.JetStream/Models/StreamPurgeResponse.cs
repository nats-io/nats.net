namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.PURGE API
/// </summary>

public record StreamPurgeResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool Success { get; set; } = default!;

    /// <summary>
    /// Number of messages purged from the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("purged")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Purged { get; set; } = default!;
}
