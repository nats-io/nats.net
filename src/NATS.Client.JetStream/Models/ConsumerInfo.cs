namespace NATS.Client.JetStream.Models;

public record ConsumerInfo
{
    /// <summary>
    /// The Stream the consumer belongs to
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
    /// A unique name for the consumer, either machine generated or the durable name
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
    /// The server time the consumer info was created
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset Ts { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("config")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public ConsumerConfig Config { get; set; } = new ConsumerConfig();

    /// <summary>
    /// The time the Consumer was created
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("created")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    public DateTimeOffset Created { get; set; }

    /// <summary>
    /// The last message delivered from this Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("delivered")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public SequenceInfo Delivered { get; set; } = new SequenceInfo();

    /// <summary>
    /// The highest contiguous acknowledged message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ack_floor")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public SequenceInfo AckFloor { get; set; } = new SequenceInfo();

    /// <summary>
    /// The number of messages pending acknowledgement
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_ack_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long NumAckPending { get; set; }

    /// <summary>
    /// The number of redeliveries that have been performed
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_redelivered")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long NumRedelivered { get; set; }

    /// <summary>
    /// The number of pull consumers waiting for messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_waiting")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long NumWaiting { get; set; }

    /// <summary>
    /// The number of messages left unconsumed in this Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long NumPending { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("cluster")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ClusterInfo? Cluster { get; set; }

    /// <summary>
    /// Indicates if any client is connected and receiving messages from a push consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("push_bound")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PushBound { get; set; }
}
