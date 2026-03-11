using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Models;

public record StreamConfig
{
    public StreamConfig(string name, ICollection<string> subjects)
    {
        Name = name;
        Subjects = subjects;
    }

    public StreamConfig()
    {
    }

    /// <summary>
    /// A unique name for the Stream, empty for Stream Templates.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[^.*>]*$")]
    public string? Name { get; set; }

    /// <summary>
    /// A short description of the purpose of this stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string? Description { get; set; }

    /// <summary>
    /// A list of subjects to consume, supports wildcards. Must be empty when a mirror is configured. May be empty when sources are configured.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? Subjects { get; set; }

    /// <summary>
    /// Subject transform to apply to matching messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject_transform")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public SubjectTransform? SubjectTransform { get; set; }

    /// <summary>
    /// How messages are retained in the Stream, once this is exceeded old messages are removed.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("retention")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<StreamConfigRetention>))]
#endif
    public StreamConfigRetention Retention { get; set; } = StreamConfigRetention.Limits;

    /// <summary>
    /// How many Consumers can be defined for a given Stream. -1 for unlimited.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(int.MinValue, int.MaxValue)]
    public int MaxConsumers { get; set; }

    /// <summary>
    /// How many messages may be in a Stream, oldest messages will be removed if the Stream exceeds this size. -1 for unlimited.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_msgs")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MaxMsgs { get; set; }

    /// <summary>
    /// For wildcard streams ensure that for every unique subject this many messages are kept - a per subject retention limit
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_msgs_per_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(int.MinValue, int.MaxValue)]
    public long MaxMsgsPerSubject { get; set; }

    /// <summary>
    /// How big the Stream may be, when the combined stream size exceeds this old messages are removed. -1 for unlimited.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MinValue)]
    public long MaxBytes { get; set; }

    /// <summary>
    /// Maximum age of any message in the stream, expressed in nanoseconds. 0 for unlimited.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_age")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan MaxAge { get; set; }

    /// <summary>
    /// The largest message that will be accepted by the Stream. -1 for unlimited.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_msg_size")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-2147483648, 2147483647)]
    public int MaxMsgSize { get; set; }

    /// <summary>
    /// The storage backend to use for the Stream.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<StreamConfigStorage>))]
#endif
    public StreamConfigStorage Storage { get; set; } = StreamConfigStorage.File;

    /// <summary>
    /// Optional compression algorithm used for the Stream.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("compression")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<StreamConfigCompression>))]
#endif
    public StreamConfigCompression Compression { get; set; } = StreamConfigCompression.None;

    /// <summary>
    /// How many replicas to keep for each message.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_replicas")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(int.MinValue, int.MaxValue)]
    public int NumReplicas { get; set; }

    /// <summary>
    /// Disables acknowledging messages that are received by the Stream.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_ack")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoAck { get; set; }

    /// <summary>
    /// When the Stream is managed by a Stream Template this identifies the template that manages the Stream.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("template_owner")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? TemplateOwner { get; set; }

    /// <summary>
    /// When a Stream reach it's limits either old messages are deleted or new ones are denied
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("discard")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<StreamConfigDiscard>))]
#endif
    public StreamConfigDiscard Discard { get; set; } = StreamConfigDiscard.Old;

    /// <summary>
    /// The time window to track duplicate messages for, expressed in nanoseconds. 0 for default
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("duplicate_window")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan DuplicateWindow { get; set; }

    /// <summary>
    /// Placement directives to consider when placing replicas of this stream, random placement when unset
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("placement")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public Placement? Placement { get; set; }

    /// <summary>
    /// Maintains a 1:1 mirror of another stream with name matching this property.  When a mirror is configured subjects and sources must be empty.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("mirror")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public StreamSource? Mirror { get; set; }

    /// <summary>
    /// List of Stream names to replicate into this Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("sources")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<StreamSource>? Sources { get; set; }

    /// <summary>
    /// Sealed streams do not allow messages to be deleted via limits or API, sealed streams can not be unsealed via configuration update. Can only be set on already created streams via the Update API
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("sealed")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Sealed { get; set; }

    /// <summary>
    /// Restricts the ability to delete messages from a stream via the API. Cannot be changed once set to true
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deny_delete")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DenyDelete { get; set; }

    /// <summary>
    /// Restricts the ability to purge messages from a stream via the API. Cannot be change once set to true
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deny_purge")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DenyPurge { get; set; }

    /// <summary>
    /// Allows the use of the Nats-Rollup header to replace all contents of a stream, or subject in a stream, with a single new message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("allow_rollup_hdrs")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowRollupHdrs { get; set; }

    /// <summary>
    /// Allow higher performance, direct access to get individual messages
    /// </summary>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.JetStream.Extensions">Batch direct message retrieval via GetBatchDirectAsync</seealso>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.Counters">Distributed counters require direct-access streams</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("allow_direct")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowDirect { get; set; }

    /// <summary>
    /// Allow higher performance, direct access for mirrors as well
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("mirror_direct")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool MirrorDirect { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("republish")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public Republish? Republish { get; set; }

    /// <summary>
    /// When discard policy is new and the stream is one with max messages per subject set, this will apply the new behavior to every subject. Essentially turning discard new from maximum number of subjects into maximum number of messages in a subject.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("discard_new_per_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DiscardNewPerSubject { get; set; }

    /// <summary>
    /// AllowMsgTTL allows header initiated per-message TTLs. If disabled, then the `NATS-TTL` header will be ignored.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.JetStream.Extensions">Scheduled and delayed message publishing</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("allow_msg_ttl")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowMsgTTL { get; set; }

    /// <summary>
    /// Enables and sets a duration for adding server markers for delete, purge and max age limits.
    /// </summary>
    /// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("subject_delete_marker_ttl")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan SubjectDeleteMarkerTTL { get; set; }

    /// <summary>
    /// Additional metadata for the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Allow message counter.
    /// </summary>
    /// <remarks>Supported by server v2.12</remarks>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.Counters">Distributed counter operations via NatsJSCounter</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("allow_msg_counter")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowMsgCounter { get; set; }

    /// <summary>
    /// AllowMsgSchedules enables the scheduling of messages.
    /// </summary>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.JetStream.Extensions">PublishScheduledAsync for delayed and recurring message scheduling</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("allow_msg_schedules")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowMsgSchedules { get; set; }

    /// <summary>
    /// AllowAtomicPublish allows atomic batch publishing into the stream.
    /// </summary>
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.JetStream.Publisher">Atomic batch publishing via PublishMsgBatchAsync</seealso>
    [System.Text.Json.Serialization.JsonPropertyName("allow_atomic")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowAtomicPublish { get; set; }

    /// <summary>
    /// PersistMode allows to opt-in to different persistence mode settings.
    /// </summary>
    /// <remarks>Supported by server v2.12</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("persist_mode")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
#if NET6_0
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonStringEnumConverter<StreamConfigPersistMode>))]
#endif
    public StreamConfigPersistMode? PersistMode { get; set; }
}
