namespace NATS.Client.JetStream.Models;

/// <summary>
/// Defines a source where streams should be replicated from
/// </summary>

public record StreamSource
{
    /// <summary>
    /// Stream name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Sequence to start replicating from
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("opt_start_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long OptStartSeq { get; set; } = default!;

    /// <summary>
    /// Time stamp to start replicating from
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("opt_start_time")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.DateTimeOffset OptStartTime { get; set; } = default!;

    /// <summary>
    /// Replicate only a subset of messages based on filter
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string FilterSubject { get; set; } = default!;

    /// <summary>
    /// Map matching subjects according to this transform destination
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject_transform_dest")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string SubjectTransformDest { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("external")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ExternalStreamSource External { get; set; } = default!;
}
