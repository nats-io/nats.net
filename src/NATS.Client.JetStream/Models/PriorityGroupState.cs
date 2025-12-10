namespace NATS.Client.JetStream.Models;

/// <summary>
/// Represents the runtime state of a priority group.
/// </summary>
/// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
public record PriorityGroupState
{
    /// <summary>
    /// The name of the priority group.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("group")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Group { get; set; }

    /// <summary>
    /// The generated ID of the pinned client (for PinnedClient policy).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("pinned_client_id")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? PinnedClientId { get; set; }

    /// <summary>
    /// The timestamp when the client was pinned.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("pinned_ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? PinnedTs { get; set; }
}
