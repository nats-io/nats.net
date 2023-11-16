namespace NATS.Client.JetStream.Models;

/// <summary>
/// The data structure that describe the configuration of a NATS JetStream Stream Template
/// </summary>

public record StreamTemplateConfig
{
    /// <summary>
    /// A unique name for the Stream Template.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
    public string Name { get; set; } = default!;

    /// <summary>
    /// The maximum number of Streams this Template can create, -1 for unlimited.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_streams")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-2147483648, 2147483647)]
    public int MaxStreams { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("config")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public StreamConfig Config { get; set; } = new StreamConfig();
}
