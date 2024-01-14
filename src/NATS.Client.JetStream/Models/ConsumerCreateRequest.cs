namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.CONSUMER.CREATE and $JS.API.CONSUMER.DURABLE.CREATE APIs
/// </summary>

internal record ConsumerCreateRequest
{
    /// <summary>
    /// The name of the stream to create the consumer in
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("stream_name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string StreamName { get; set; } = default!;
#else
    public required string StreamName { get; set; }
#endif

    /// <summary>
    /// The consumer configuration
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("config")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public ConsumerConfig? Config { get; set; }
}
