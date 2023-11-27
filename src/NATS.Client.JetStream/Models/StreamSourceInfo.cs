using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Models;

/// <summary>
/// Information about an upstream stream source in a mirror
/// </summary>

public record StreamSourceInfo
{
    /// <summary>
    /// The name of the Stream being replicated
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// The subject filter to apply to the messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string FilterSubject { get; set; } = default!;

    /// <summary>
    /// The subject transform destination to apply to the messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject_transform_dest")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string SubjectTransformDest { get; set; } = default!;

    /// <summary>
    /// How many messages behind the mirror operation is
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("lag")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Lag { get; set; } = default!;

    /// <summary>
    /// When last the mirror had activity, in nanoseconds. Value will be -1 when there has been no activity.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("active")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan Active { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("external")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ExternalStreamSource External { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("error")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ApiError Error { get; set; } = default!;
}
