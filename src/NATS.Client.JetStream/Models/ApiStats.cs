namespace NATS.Client.JetStream.Models;

public record ApiStats
{
    /// <summary>
    /// Supported API level.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("level")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Level { get; set; }

    /// <summary>
    /// Total number of API requests received for this account
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("total")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong Total { get; set; }

    /// <summary>
    /// API requests that resulted in an error response
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("errors")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong Errors { get; set; }

    /// <summary>
    /// Inflight API requests
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("inflight")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong Inflight { get; set; }
}
