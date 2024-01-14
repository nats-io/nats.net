namespace NATS.Client.JetStream.Models;

public record StreamInfo
{
    /// <summary>
    /// The active configuration for the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("config")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public StreamConfig Config { get; set; } = new StreamConfig();

    /// <summary>
    /// Detail about the current State of the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public StreamState State { get; set; } = new StreamState();

    /// <summary>
    /// Timestamp when the stream was created
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("created")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    public DateTimeOffset Created { get; set; }

    /// <summary>
    /// The server time the stream info was created
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset Ts { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("cluster")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ClusterInfo? Cluster { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("mirror")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public StreamSourceInfo? Mirror { get; set; }

    /// <summary>
    /// Streams being sourced into this Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("sources")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<StreamSourceInfo>? Sources { get; set; }

    /// <summary>
    /// List of mirrors sorted by priority
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("alternates")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<StreamAlternate>? Alternates { get; set; }
}
