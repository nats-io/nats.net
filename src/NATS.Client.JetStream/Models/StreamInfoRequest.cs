namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.INFO API
/// </summary>

public record StreamInfoRequest
{
    /// <summary>
    /// When true will result in a full list of deleted message IDs being returned in the info response
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deleted_details")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DeletedDetails { get; set; }

    /// <summary>
    /// When set will return a list of subjects and how many messages they hold for all matching subjects. Filter is a standard NATS subject wildcard pattern.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subjects_filter")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? SubjectsFilter { get; set; }

    /// <summary>
    /// Paging offset when retrieving pages of subject details
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("offset")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Offset { get; set; }
}
