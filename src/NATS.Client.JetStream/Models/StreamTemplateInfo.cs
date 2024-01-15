namespace NATS.Client.JetStream.Models;

public record StreamTemplateInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("config")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
#if NET6_0
    public StreamTemplateConfig Config { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required StreamTemplateConfig Config { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// List of Streams managed by this Template
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("streams")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public ICollection<string> Streams { get; set; } = new System.Collections.ObjectModel.Collection<string>();
}
