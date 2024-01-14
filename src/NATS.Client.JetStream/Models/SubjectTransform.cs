namespace NATS.Client.JetStream.Models;

/// <summary>
/// Subject transform to apply to matching messages going into the stream
/// </summary>

public record SubjectTransform
{
    /// <summary>
    /// The subject transform source
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("src")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Src { get; set; }

    /// <summary>
    /// The subject transform destination
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
}
