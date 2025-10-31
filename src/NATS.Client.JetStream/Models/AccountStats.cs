namespace NATS.Client.JetStream.Models;

public record AccountStats
{
    /// <summary>
    /// Memory Storage being used for Stream Message storage
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("memory")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong Memory { get; set; }

    /// <summary>
    /// File Storage being used for Stream Message storage
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong Storage { get; set; }

    /// <summary>
    /// Number of active Streams
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("streams")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Streams { get; set; }

    /// <summary>
    /// Number of active Consumers
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Consumers { get; set; }

    /// <summary>
    /// The JetStream domain this account is in
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("domain")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Domain { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("limits")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public AccountLimits Limits { get; set; } = new AccountLimits();

    [System.Text.Json.Serialization.JsonPropertyName("tiers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, Tier>? Tiers { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("api")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public ApiStats Api { get; set; } = new ApiStats();
}
