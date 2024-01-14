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
#if NET6_0
    public string Name { get; set; } = default!;
#else
    public required string Name { get; set; }
#endif

    /// <summary>
    /// Sequence to start replicating from
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("opt_start_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long OptStartSeq { get; set; }

    /// <summary>
    /// Time stamp to start replicating from
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("opt_start_time")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset OptStartTime { get; set; }

    /// <summary>
    /// Replicate only a subset of messages based on filter
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? FilterSubject { get; set; }

    /// <summary>
    /// Subject transforms to apply to matching messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject_transforms")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<SubjectTransform>? SubjectTransforms { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("external")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ExternalStreamSource? External { get; set; }
}
