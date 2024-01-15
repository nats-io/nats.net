namespace NATS.Client.JetStream.Models;

/// <summary>
/// An alternate location to read mirrored data
/// </summary>

public record StreamAlternate
{
    /// <summary>
    /// The mirror stream name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string Name { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Name { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// The name of the cluster holding the stream
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
    /// The domain holding the string
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("domain")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Domain { get; set; }
}
