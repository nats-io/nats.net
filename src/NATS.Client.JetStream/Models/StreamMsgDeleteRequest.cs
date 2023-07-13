namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.MSG.DELETE API
/// </summary>

public record StreamMsgDeleteRequest
{
    /// <summary>
    /// Stream sequence number of the message to delete
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Seq { get; set; } = default!;

    /// <summary>
    /// Default will securely remove a message and rewrite the data with random data, set this to true to only remove the message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_erase")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoErase { get; set; } = default!;
}
