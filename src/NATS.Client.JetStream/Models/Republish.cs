namespace NATS.Client.JetStream.Models;

/// <summary>
/// Rules for republishing messages from a stream with subject mapping onto new subjects for partitioning and more
/// </summary>

public record Republish
{
    /// <summary>
    /// The source subject to republish
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("src")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string Src { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Src { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// The destination to publish to
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("dest")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string Dest { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Dest { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// Only send message headers, no bodies
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("headers_only")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeadersOnly { get; set; }
}
