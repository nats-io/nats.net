using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Models;

public record ConsumerConfig
{
    /// <summary>
    /// Create an ephemeral consumer configuration.
    /// </summary>
    /// <remarks>
    /// When <c>DurableName</c> is not specified, the consumer is ephemeral.
    /// This default constructor doesn't set any properties explicitly.
    /// </remarks>
    public ConsumerConfig()
    {
    }

    /// <summary>
    /// Create a durable consumer configuration.
    /// </summary>
    /// <param name="name">Durable name</param>
    /// <remarks>
    /// To create a durable consumer, the properties <c>Name</c> and <c>DurableName</c>
    /// must be specified. You must also specify the <c>AckPolicy</c> as other than <c>None</c>.
    /// This constructor sets the <c>Name</c> and <c>DurableName</c> to the same value specified
    /// in <paramref name="name"/> parameter and sets the <c>AckPolicy</c> to <c>Explicit</c>.
    /// </remarks>
    public ConsumerConfig(string name)
    {
        Name = name;
        DurableName = name;
    }

    [System.Text.Json.Serialization.JsonPropertyName("deliver_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<ConsumerConfigDeliverPolicy>))]
#endif
    public ConsumerConfigDeliverPolicy DeliverPolicy { get; set; } = ConsumerConfigDeliverPolicy.All;

    [System.Text.Json.Serialization.JsonPropertyName("opt_start_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong OptStartSeq { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("opt_start_time")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset OptStartTime { get; set; }

    /// <summary>
    /// A unique name for a durable consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("durable_name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
    public string? DurableName { get; set; }

    /// <summary>
    /// A unique name for a consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
    public string? Name { get; set; }

    /// <summary>
    /// A short description of the purpose of this consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string? Description { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("deliver_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    public string? DeliverSubject { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("deliver_group")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    public string? DeliverGroup { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("ack_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<ConsumerConfigAckPolicy>))]
#endif
    public ConsumerConfigAckPolicy AckPolicy { get; set; } = ConsumerConfigAckPolicy.Explicit;

    /// <summary>
    /// How long (in nanoseconds) to allow messages to remain un-acknowledged before attempting redelivery
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ack_wait")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan AckWait { get; set; }

    /// <summary>
    /// The number of times a message will be redelivered to consumers if not acknowledged in time
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_deliver")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MaxDeliver { get; set; }

    /// <summary>
    /// Filter the stream by a single subjects
    /// </summary>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.PCGroups">Partitioned consumer groups with subject-based partitioning</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? FilterSubject { get; set; }

    /// <summary>
    /// Filter the stream by multiple subjects
    /// </summary>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.PCGroups">Partitioned consumer groups with multi-subject partitioning</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? FilterSubjects { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("replay_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<ConsumerConfigReplayPolicy>))]
#endif
    public ConsumerConfigReplayPolicy ReplayPolicy { get; set; } = ConsumerConfigReplayPolicy.Instant;

    [System.Text.Json.Serialization.JsonPropertyName("sample_freq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? SampleFreq { get; set; }

    /// <summary>
    /// The rate at which messages will be delivered to clients, expressed in bit per second
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("rate_limit_bps")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0L, long.MaxValue)]
    public long RateLimitBps { get; set; }

    /// <summary>
    /// The maximum number of messages without acknowledgement that can be outstanding, once this limit is reached message delivery will be suspended
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_ack_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MaxAckPending { get; set; }

    /// <summary>
    /// If the Consumer is idle for more than this many nano seconds a empty message with Status header 100 will be sent indicating the consumer is still alive
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("idle_heartbeat")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan IdleHeartbeat { get; set; }

    /// <summary>
    /// For push consumers this will regularly send an empty mess with Status header 100 and a reply subject, consumers must reply to these messages to control the rate of message delivery
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("flow_control")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FlowControl { get; set; }

    /// <summary>
    /// The number of pulls that can be outstanding on a pull consumer, pulls received after this is reached are ignored
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_waiting")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MaxWaiting { get; set; }

    /// <summary>
    /// Creates a special consumer that does not touch the Raft layers, not for general use by clients, internal use only
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("direct")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Direct { get; set; }

    /// <summary>
    /// Delivers only the headers of messages in the stream and not the bodies. Additionally adds Nats-Msg-Size header to indicate the size of the removed payload
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("headers_only")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeadersOnly { get; set; }

    /// <summary>
    /// The largest batch property that may be specified when doing a pull on a Pull Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_batch")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxBatch { get; set; }

    /// <summary>
    /// The maximum expires value that may be set when doing a pull on a Pull Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_expires")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan MaxExpires { get; set; }

    /// <summary>
    /// The maximum bytes value that maybe set when dong a pull on a Pull Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MaxBytes { get; set; }

    /// <summary>
    /// Duration that instructs the server to cleanup ephemeral consumers that are inactive for that long
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("inactive_threshold")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan InactiveThreshold { get; set; }

    /// <summary>
    /// List of durations in Go format that represents a retry time scale for NaK'd messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("backoff")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNullableCollectionNanosecondsConverter))]
    public ICollection<TimeSpan>? Backoff { get; set; }

    /// <summary>
    /// When set do not inherit the replica count from the stream but specifically set it to this amount
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_replicas")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long NumReplicas { get; set; }

    /// <summary>
    /// If the consumer is paused, this contains until which time it is paused.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("pause_until")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? PauseUntil { get; set; }

    /// <summary>
    /// Force the consumer state to be kept in memory rather than inherit the setting from the stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("mem_storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool MemStorage { get; set; }

    /// <summary>
    /// Additional metadata for the Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Defines a collection of priority groups for a pull consumer.
    /// </summary>
    /// <remarks>
    /// The priority group names must conform to the requirements outlined in ADR-6: alphanumeric characters, dashes, underscores, forward slashes, or equals signs are allowed, with a maximum length of 16 characters per group.
    /// Configuring this property for a push consumer will result in an error.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("priority_groups")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    public ICollection<string>? PriorityGroups { get; set; }
#else
    public ICollection<string>? PriorityGroups { get; init; }
#endif

    /// <summary>
    /// Specifies the priority policy for consumer message selection.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("priority_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<ConsumerConfigPriorityPolicy>))]
    public ConsumerConfigPriorityPolicy? PriorityPolicy { get; set; }
#else
    public ConsumerConfigPriorityPolicy? PriorityPolicy { get; init; }
#endif

    /// <summary>
    /// Specifies the duration for which the consumer's pinned message priority is maintained.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("priority_timeout")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNullableNanosecondsConverter))]
#if NET6_0
    public TimeSpan? PinnedTTL { get; set; }
#else
    public TimeSpan? PinnedTTL { get; init; }
#endif
}
