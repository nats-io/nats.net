namespace NATS.Client.JetStream.Models;

public record ConsumerConfig
{
    public ConsumerConfig()
    {
    }

    public ConsumerConfig(string name)
    {
        Name = name;
        DurableName = name;
        AckPolicy = ConsumerConfigAckPolicy.@explicit;
    }

    [System.Text.Json.Serialization.JsonPropertyName("deliver_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ConsumerConfigDeliverPolicy DeliverPolicy { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("opt_start_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public ulong OptStartSeq { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("opt_start_time")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.DateTimeOffset OptStartTime { get; set; } = default!;

    /// <summary>
    /// A unique name for a durable consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("durable_name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
    public string DurableName { get; set; } = default!;

    /// <summary>
    /// A unique name for a consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]+$")]
    public string Name { get; set; } = default!;

    /// <summary>
    /// A short description of the purpose of this consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string Description { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("deliver_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue, MinimumLength = 1)]
    public string DeliverSubject { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("ack_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ConsumerConfigAckPolicy AckPolicy { get; set; } = ConsumerConfigAckPolicy.none;

    /// <summary>
    /// How long (in nanoseconds) to allow messages to remain un-acknowledged before attempting redelivery
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ack_wait")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long AckWait { get; set; } = default!;

    /// <summary>
    /// The number of times a message will be redelivered to consumers if not acknowledged in time
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_deliver")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long MaxDeliver { get; set; } = default!;

    /// <summary>
    /// Filter the stream by a single subjects
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string FilterSubject { get; set; } = default!;

    /// <summary>
    /// Filter the stream by multiple subjects
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("filter_subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.ICollection<string> FilterSubjects { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("replay_policy")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ConsumerConfigReplayPolicy ReplayPolicy { get; set; } = NATS.Client.JetStream.Models.ConsumerConfigReplayPolicy.instant;

    [System.Text.Json.Serialization.JsonPropertyName("sample_freq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string SampleFreq { get; set; } = default!;

    /// <summary>
    /// The rate at which messages will be delivered to clients, expressed in bit per second
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("rate_limit_bps")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long RateLimitBps { get; set; } = default!;

    /// <summary>
    /// The maximum number of messages without acknowledgement that can be outstanding, once this limit is reached message delivery will be suspended
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_ack_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long MaxAckPending { get; set; } = default!;

    /// <summary>
    /// If the Consumer is idle for more than this many nano seconds a empty message with Status header 100 will be sent indicating the consumer is still alive
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("idle_heartbeat")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long IdleHeartbeat { get; set; } = default!;

    /// <summary>
    /// For push consumers this will regularly send an empty mess with Status header 100 and a reply subject, consumers must reply to these messages to control the rate of message delivery
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("flow_control")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FlowControl { get; set; } = default!;

    /// <summary>
    /// The number of pulls that can be outstanding on a pull consumer, pulls received after this is reached are ignored
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_waiting")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long MaxWaiting { get; set; } = default!;

    /// <summary>
    /// Creates a special consumer that does not touch the Raft layers, not for general use by clients, internal use only
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("direct")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Direct { get; set; } = false;

    /// <summary>
    /// Delivers only the headers of messages in the stream and not the bodies. Additionally adds Nats-Msg-Size header to indicate the size of the removed payload
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("headers_only")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeadersOnly { get; set; } = false;

    /// <summary>
    /// The largest batch property that may be specified when doing a pull on a Pull Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_batch")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxBatch { get; set; } = 0;

    /// <summary>
    /// The maximum expires value that may be set when doing a pull on a Pull Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_expires")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long MaxExpires { get; set; } = default!;

    /// <summary>
    /// The maximum bytes value that maybe set when dong a pull on a Pull Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long MaxBytes { get; set; } = default!;

    /// <summary>
    /// Duration that instructs the server to cleanup ephemeral consumers that are inactive for that long
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("inactive_threshold")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long InactiveThreshold { get; set; } = default!;

    /// <summary>
    /// List of durations in Go format that represents a retry time scale for NaK'd messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("backoff")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.ICollection<long> Backoff { get; set; } = default!;

    /// <summary>
    /// When set do not inherit the replica count from the stream but specifically set it to this amount
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_replicas")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long NumReplicas { get; set; } = default!;

    /// <summary>
    /// Force the consumer state to be kept in memory rather than inherit the setting from the stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("mem_storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool MemStorage { get; set; } = false;

    /// <summary>
    /// Additional metadata for the Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.IDictionary<string, string> Metadata { get; set; } = default!;
}
