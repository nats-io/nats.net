using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Models;

public record PeerInfo
{
    /// <summary>
    /// The server name of the peer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string Name { get; set; } = default!;
#else
    public required string Name { get; set; }
#endif

    /// <summary>
    /// Indicates if the server is up to date and synchronised
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("current")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool Current { get; set; }

    /// <summary>
    /// Nanoseconds since this peer was last seen
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("active")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan Active { get; set; }

    /// <summary>
    /// Indicates the node is considered offline by the group
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("offline")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Offline { get; set; }

    /// <summary>
    /// How many uncommitted operations this peer is behind the leader
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("lag")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Lag { get; set; }
}
