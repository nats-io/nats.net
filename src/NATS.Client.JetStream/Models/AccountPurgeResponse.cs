namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.ACCOUNT.PURGE API
/// </summary>

public record AccountPurgeResponse
{
    /// <summary>
    /// If the purge operation was successfully started
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("initiated")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Initiated { get; set; }
}
