using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// JetStream options to be used within a JetStream context.
/// </summary>
public record NatsJSOpts
{
    public NatsJSOpts(NatsOpts opts, string? apiPrefix = default, string? domain = default, AckOpts? ackOpts = default)
    {
        if (apiPrefix != null && domain != null)
        {
            throw new NatsJSException("Cannot specify both ApiPrefix and Domain.");
        }

        ApiPrefix = apiPrefix ?? "$JS.API";
        AckOpts = ackOpts ?? new AckOpts(opts.WaitUntilSent);
        Domain = domain;
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
    /// Message ACK options <see cref="AckOpts"/>.
    /// </summary>
    /// <remarks>
    /// These options are used as the defaults when acknowledging messages received from a stream using a consumer.
    /// </remarks>
    public AckOpts AckOpts { get; init; }

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

    public Func<INatsJSNotification, CancellationToken, Task>? NotificationHandler { get; init; }
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
}

/// <summary>
/// Consumer fetch method options.
/// </summary>
public record NatsJSFetchOpts
{
    public NatsJSFetchOpts(int? maxMsgs) => MaxMsgs = maxMsgs;

    /// <summary>
    /// Maximum number of messages to return
    /// </summary>
    public int? MaxMsgs { get; }

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

    // Publish retries for NoResponders err.
    // rwait time.Duration // Retry wait between attempts
    public TimeSpan RetryWaitBetweenAttempts { get; init; } = TimeSpan.FromMilliseconds(250);

    // rnum  int           // Retry attempts
    public int RetryAttempts { get; init; } = 2;
}
