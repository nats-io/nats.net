namespace NATS.Client.JetStream.Models;

public record Tier
{
    /// <summary>
    /// Memory Storage being used for Stream Message storage
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("memory")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Memory { get; set; }

    /// <summary>
    /// File Storage being used for Stream Message storage
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Storage { get; set; }

    /// <summary>
    /// Reserved Memory for Stream Message storage
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("reserved_memory")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong ReservedMemory { get; set; }

    /// <summary>
    /// Reserved Memory for Stream Message storage
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("reserved_storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, ulong.MaxValue)]
    public ulong ReservedStore { get; set; }

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

    [System.Text.Json.Serialization.JsonPropertyName("limits")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public AccountLimits Limits { get; set; } = new AccountLimits();
}
