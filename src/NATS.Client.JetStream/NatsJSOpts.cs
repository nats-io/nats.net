using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// JetStream options to be used within a JetStream context.
/// </summary>
public record NatsJSOpts
{
    public NatsJSOpts(NatsOpts opts, string? apiPrefix = default, string? domain = default, AckOpts? ackOpts = default, TimeSpan? requestTimeout = default)
    {
        if (apiPrefix != null && domain != null)
        {
            throw new NatsJSException("Cannot specify both ApiPrefix and Domain.");
        }

        ApiPrefix = apiPrefix ?? "$JS.API";
        Domain = domain;
        RequestTimeout = requestTimeout ?? opts.RequestTimeout;
    }

    /// <summary>
    /// Complete prefix to prepend to JetStream API subjects as it's dynamically built from ApiPrefix and Domain properties.
    /// </summary>
    public string Prefix => string.IsNullOrEmpty(Domain) ? ApiPrefix : $"$JS.{Domain}.API";

    /// <summary>
    /// Prefix to prepend to JetStream API subjects. (default: $JS.API)
    /// </summary>
    public string ApiPrefix { get; }

    /// <summary>
    /// JetStream domain to use in JetStream API subjects. (default: null)
    /// </summary>
    public string? Domain { get; }

    /// <summary>
    /// Timeout for JetStream API calls.
    /// </summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>
    /// Ask server for an acknowledgment.
    /// </summary>
    /// <remarks>
    /// Defaults to false.
    /// </remarks>
    public bool DoubleAck { get; init; } = false;

    /// <summary>
    /// Default consume options to be used in consume calls in this context.
    /// </summary>
    /// <remarks>
    /// Defaults to MaxMsgs = 1,000.
    /// </remarks>
    public NatsJSConsumeOpts DefaultConsumeOpts { get; init; } = new() { MaxMsgs = 1_000 };

    /// <summary>
    /// Default next options to be used in next calls in this context.
    /// </summary>
    public NatsJSNextOpts DefaultNextOpts { get; init; } = new();
}

public record NatsJSOrderedConsumerOpts
{
    public static readonly NatsJSOrderedConsumerOpts Default = new();

    public string[] FilterSubjects { get; init; } = Array.Empty<string>();

    public ConsumerConfigDeliverPolicy DeliverPolicy { get; init; } = ConsumerConfigDeliverPolicy.All;

    public ulong OptStartSeq { get; init; } = 0;

    public DateTimeOffset OptStartTime { get; init; } = default;

    public ConsumerConfigReplayPolicy ReplayPolicy { get; init; } = ConsumerConfigReplayPolicy.Instant;

    public TimeSpan InactiveThreshold { get; init; } = TimeSpan.FromMinutes(5);

    public bool HeadersOnly { get; init; } = false;

    /// <summary>
    /// Maximum number of attempts for the consumer to be recreated (Defaults to unlimited).
    /// </summary>
    public int MaxResetAttempts { get; init; } = -1;
}

/// <summary>
/// Consumer consume method options.
/// </summary>
public record NatsJSConsumeOpts
{
    /// <summary>
    /// Maximum number of messages stored in the buffer
    /// </summary>
    public int? MaxMsgs { get; init; }

    /// <summary>
    /// Amount of time to wait for a single pull request to expire
    /// </summary>
    public TimeSpan? Expires { get; init; }

    /// <summary>
    /// Maximum number of bytes stored in the buffer
    /// </summary>
    public int? MaxBytes { get; init; }

    /// <summary>
    /// Amount idle time the server should wait before sending a heartbeat
    /// </summary>
    public TimeSpan? IdleHeartbeat { get; init; }

    /// <summary>
    /// Number of messages left in the buffer that should trigger a low watermark on the client, and influence it to request more messages
    /// </summary>
    public int? ThresholdMsgs { get; init; }

    /// <summary>
    /// Hint for the number of bytes left in buffer that should trigger a low watermark on the client, and influence it to request more data.
    /// </summary>
    public int? ThresholdBytes { get; init; }

    /// <summary>
    /// A handler function invoked for notifications related to consumption, allowing custom handling of
    /// operational updates or state changes during the consumer's lifecycle.
    /// </summary>
    /// <remarks>
    /// Possible notifications include:
    /// <para>
    /// <see cref="NatsJSProtocolNotification"/> is sent when the server sends a header not handled by the client.
    /// </para>
    /// <para>
    /// <see cref="NatsJSMessageSizeExceedsMaxBytesNotification"/> is sent when a message exceeds the maximum bytes limit.
    /// </para>
    /// <para>
    /// <see cref="NatsJSLeadershipChangeNotification"/> is sent when the JetStream leadership changes.
    /// </para>
    /// <para>
    /// <see cref="NatsJSNoRespondersNotification"/> is sent when there are no responders for a request.
    /// This is a generic NATS server 503 error which may be caused by various reasons, however, in most cases
    /// it will indicate that the cluster's state is changing and a server is restarting.
    /// </para>
    /// <para>
    /// <see cref="NatsJSTimeoutNotification"/> is sent when the client times out waiting for a response.
    /// It is triggered when the server does not respond within twice the idle heartbeat duration.
    /// </para>
    /// </remarks>
    public Func<INatsJSNotification, CancellationToken, Task>? NotificationHandler { get; init; }

    /// <summary>
    /// Optional priority group configuration used for consuming messages.
    /// Defines a group name and constraints for minimum pending messages and acknowledgments.
    /// </summary>
    public NatsJSPriorityGroupOpts? PriorityGroup { get; init; }

    /// <summary>
    /// Maximum number of consecutive 503 "No Responders" errors before the consumer is considered deleted and consumption stops.
    /// This helps detect when an ephemeral consumer has vanished on the server.
    /// Set to -1 to disable this check. (default: 10)
    /// </summary>
    public int MaxConsecutive503Errors { get; init; } = 10;
}

/// <summary>
/// Consumer next method options.
/// </summary>
public record NatsJSNextOpts
{
    /// <summary>
    /// Amount of time to wait for the request to expire (in nanoseconds)
    /// </summary>
    public TimeSpan? Expires { get; init; }

    /// <summary>
    /// Amount idle time the server should wait before sending a heartbeat. For requests with expires > 30s, heartbeats should be enabled by default
    /// </summary>
    public TimeSpan? IdleHeartbeat { get; init; }

    public Func<INatsJSNotification, CancellationToken, Task>? NotificationHandler { get; init; }

    /// <summary>
    /// Optional priority group configuration used for consuming messages.
    /// Defines a group name and constraints for minimum pending messages and acknowledgments.
    /// </summary>
    public NatsJSPriorityGroupOpts? PriorityGroup { get; init; }
}

/// <summary>
/// Consumer fetch method options.
/// </summary>
public record NatsJSFetchOpts
{
    /// <summary>
    /// Maximum number of messages to return
    /// </summary>
    public int? MaxMsgs { get; init; }

    /// <summary>
    /// Amount of time to wait for the request to expire
    /// </summary>
    public TimeSpan? Expires { get; init; }

    /// <summary>
    /// Maximum number of bytes to return
    /// </summary>
    public int? MaxBytes { get; init; }

    /// <summary>
    /// Amount idle time the server should wait before sending a heartbeat. For requests with expires > 30s, heartbeats should be enabled by default
    /// </summary>
    public TimeSpan? IdleHeartbeat { get; init; }

    public Func<INatsJSNotification, CancellationToken, Task>? NotificationHandler { get; init; }

    /// <summary>
    /// Optional priority group configuration used for consuming messages.
    /// Defines a group name and constraints for minimum pending messages and acknowledgments.
    /// </summary>
    public NatsJSPriorityGroupOpts? PriorityGroup { get; init; }

    /// <summary>
    /// Does not wait for messages to be available
    /// </summary>
    internal bool NoWait { get; init; }
}

public record NatsJSPubOpts : NatsPubOpts
{
    public static readonly NatsJSPubOpts Default = new();

    // ttl time.Duration
    // id  string
    public string? MsgId { get; init; }

    // lid string  // Expected last msgId
    public string? ExpectedLastMsgId { get; init; }

    // str string  // Expected stream name
    public string? ExpectedStream { get; init; }

    // seq *uint64 // Expected last sequence
    public ulong? ExpectedLastSequence { get; init; }

    // lss *uint64 // Expected last sequence per subject
    public ulong? ExpectedLastSubjectSequence { get; init; }

    /// <summary>
    /// Specifies the duration to wait between retry attempts for a failed publish operation.
    /// See <see cref="RetryAttempts"/> for the number of retry attempts.
    /// </summary>
    // Expected last sequence subject filter (allows wildcard subjects)
    public string? ExpectedLastSubjectSequenceSubject { get; init; }

    /// <summary>
    /// Specifies the duration to wait between retry attempts for a failed publish operation.
    /// See <see cref="RetryAttempts"/> for the number of retry attempts.
    /// </summary>
    public TimeSpan RetryWaitBetweenAttempts { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Specifies the number of retry attempts to publish a message when a "NoResponders" error occurs.
    /// The value defines how many additional attempts will be made after the initial publish attempt.
    /// Default is not to retry (one attempt total).
    /// </summary>
    /// <remarks>
    /// By default, this is set to 1, meaning that if the first publish attempt fails with a "NoResponders" error,
    /// no more attempts will be made. Setting this to a higher value allows for more retries in case of transient issues.
    /// Coupled with <see cref="RetryWaitBetweenAttempts"/>, this provides a mechanism to handle temporary unavailability of responders,
    /// however, for more robust handling of such scenarios, consider implementing an exponential backoff strategy in your application logic,
    /// or use an extension that supports it.
    /// </remarks>
    public int RetryAttempts { get; init; } = 1;

    /// <summary>
    /// Sets the <c>Nats-Schedule</c> header for scheduling message delivery.
    /// </summary>
    /// <remarks>
    /// Supports cron expressions (e.g. <c>"0 0 * * *"</c>), interval patterns (e.g. <c>"@every 5m"</c>),
    /// or one-time schedules (e.g. <c>"@at 2024-01-01T00:00:00Z"</c>).
    /// Requires the stream to have <c>AllowMsgSchedules</c> enabled.
    /// </remarks>
    public string? Schedule { get; init; }

    /// <summary>
    /// Sets the <c>Nats-Schedule-Target</c> header specifying the subject where the scheduled message will be published.
    /// </summary>
    public string? ScheduleTarget { get; init; }

    /// <summary>
    /// Sets the <c>Nats-Schedule-Source</c> header specifying a subject from which to source the last message's data and headers
    /// when the schedule fires.
    /// </summary>
    /// <remarks>
    /// The source subject must be a literal (no wildcards), and must not match the schedule or target subjects.
    /// Requires the stream to have <c>AllowMsgSchedules</c> enabled.
    /// </remarks>
    public string? ScheduleSource { get; init; }

    /// <summary>
    /// Sets the <c>Nats-Schedule-TTL</c> header specifying the TTL for messages produced by the schedule.
    /// </summary>
    /// <remarks>
    /// Minimum value is 1 second. Use <see cref="TimeSpan.MaxValue"/> to indicate the message should never expire.
    /// Requires the stream to have <c>AllowMsgTTL</c> enabled.
    /// </remarks>
    public TimeSpan? ScheduleTTL { get; init; }
}

/// <summary>
/// Represents options for configuring a priority group within JetStream operations.
/// </summary>
public record NatsJSPriorityGroupOpts
{
    /// <summary>
    /// Specifies the group name for prioritization in JetStream consumer operations.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// When specified, this Pull request will only receive messages when the consumer has at least this many pending messages.
    /// </summary>
    public long MinPending { get; set; }

    /// <summary>
    /// When specified, this Pull request will only receive messages when the consumer has at least this many ack pending messages.
    /// </summary>
    public long MinAckPending { get; set; }

    /// <summary>
    /// Priority for message delivery when using prioritized priority policy.
    /// </summary>
    /// <remarks>
    /// Lower values indicate higher priority (0 is the highest priority).
    /// Maximum priority value is 9. This field is only used when the consumer
    /// has PriorityPolicy set to "prioritized".
    /// </remarks>
    public byte Priority { get; init; }
}
