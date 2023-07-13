namespace NATS.Client.JetStream.Models;

/// <summary>
/// Records messages that were damaged and unrecoverable
/// </summary>

public record LostStreamData
{
    /// <summary>
    /// The messages that were lost
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("msgs")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.ICollection<long>? Msgs { get; set; } = default!;

    /// <summary>
    /// The number of bytes that were lost
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Bytes { get; set; } = default!;
}
