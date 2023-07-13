namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.LIST API
/// </summary>

public record StreamListRequest
{
    /// <summary>
    /// Limit the list to streams matching this subject filter
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Subject { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("offset")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Offset { get; set; } = default!;
}
