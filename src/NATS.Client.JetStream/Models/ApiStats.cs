namespace NATS.Client.JetStream.Models;

public record ApiStats
{
    /// <summary>
    /// Total number of API requests received for this account
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("total")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Total { get; set; } = default!;

    /// <summary>
    /// API requests that resulted in an error response
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("errors")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Errors { get; set; } = default!;
}
