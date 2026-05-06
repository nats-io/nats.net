namespace NATS.Client.JetStream.Models;

/// <summary>
/// Identifies a pre-created durable consumer used for stream sourcing or mirroring.
/// When set on a StreamSource, the server uses this consumer instead of creating
/// an ephemeral one. Available on server 2.14+.
/// </summary>
public record StreamConsumerSource
{
    /// <summary>
    /// Name of the durable consumer to use.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
#if NET6_0
    public string Name { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Name { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// The subject the server delivers messages to.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deliver_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? DeliverSubject { get; set; }
}
