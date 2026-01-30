using NATS.Client.JetStream.Internal;

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
#pragma warning disable SA1206
    public required string StreamName { get; set; }
#pragma warning restore SA1206
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
#pragma warning disable SA1206
    public required string Name { get; set; }
#pragma warning restore SA1206
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
    [System.ComponentModel.DataAnnotations.Range(int.MinValue, int.MaxValue)]
    public int NumAckPending { get; set; }

    /// <summary>
    /// The number of redeliveries that have been performed
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_redelivered")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(int.MinValue, int.MaxValue)]
    public int NumRedelivered { get; set; }

    /// <summary>
    /// The number of pull consumers waiting for messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_waiting")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(int.MinValue, int.MaxValue)]
    public int NumWaiting { get; set; }

    /// <summary>
    /// The number of messages left unconsumed in this Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong NumPending { get; set; }

    /// <summary>
    /// Whether the consumer is paused.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("paused")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPaused { get; set; }

    /// <summary>
    /// If the consumer is <see cref="IsPaused"/>, this contains how much time is remaining until this consumer is unpaused.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("pause_remaining")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNullableNanosecondsConverter))]
    public TimeSpan? PauseRemaining { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("cluster")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ClusterInfo? Cluster { get; set; }

    /// <summary>
    /// Indicates if any client is connected and receiving messages from a push consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("push_bound")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PushBound { get; set; }

    /// <summary>
    /// Information about the currently defined priority groups.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("priority_groups")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<PriorityGroupState>? PriorityGroups { get; set; }
}
