namespace NATS.Client.JetStream.Models;

public record StreamTemplateInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("config")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public StreamTemplateConfig Config { get; set; } = new StreamTemplateConfig();

    /// <summary>
    /// List of Streams managed by this Template
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("streams")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public System.Collections.Generic.ICollection<string> Streams { get; set; } = new System.Collections.ObjectModel.Collection<string>();
}
