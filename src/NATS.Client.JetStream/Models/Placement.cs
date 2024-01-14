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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string Cluster { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Cluster { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// Tags required on servers hosting this stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? Tags { get; set; }
}
