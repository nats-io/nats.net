namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.LIST API
/// </summary>

public record StreamListResponse : IterableResponse
{
    /// <summary>
    /// Full Stream information for each known Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("streams")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public ICollection<StreamInfo> Streams { get; set; } = new System.Collections.ObjectModel.Collection<StreamInfo>();

    /// <summary>
    /// In clustered environments gathering Stream info might time out, this list would be a list of Streams for which information was not obtainable
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("missing")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? Missing { get; set; }
}
