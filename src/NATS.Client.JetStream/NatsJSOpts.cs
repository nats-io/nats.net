using NATS.Client.Core;

namespace NATS.Client.JetStream;

public record NatsJSOpts
{
    /// <summary>
    /// Prefix to prepend to JetStream API subjects. (default: $JS.API)
    /// </summary>
    public string ApiPrefix { get; init; } = "$JS.API";

    /// <summary>
    /// Prefix to use in inbox subscription subjects to receive messages from JetStream. (default: _INBOX)
    /// <para>
    /// Default is taken from NatsOptions (on the parent NatsConnection) which is '_INBOX' if not set.
    /// </para>
    /// </summary>
    public string InboxPrefix { get; init; } = string.Empty;

    /// <summary>
    /// Maximum number of messages to receive in a batch. (default: 1000)
    /// </summary>
    public int MaxMsgs { get; init; } = 1000;
}

public record NatsJSConsumeOpts
{
    /// <summary>
    /// Errors and notifications handler
    /// </summary>
    public Action<NatsJSNotification>? ErrorHandler { get; init; }

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
    /// Serializer to use to deserialize the message if a model is being used.
    /// </summary>
    /// <remarks>
    /// If not set, serializer set in connection options or the default JSON serializer
    /// will be used.
    /// </remarks>
    public INatsSerializer? Serializer { get; init; }
}

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

    /// <summary>
    /// Serializer to use to deserialize the message if a model is being used.
    /// </summary>
    /// <remarks>
    /// If not set, serializer set in connection options or the default JSON serializer
    /// will be used.
    /// </remarks>
    public INatsSerializer? Serializer { get; init; }
}

public record NatsJSFetchOpts
{
    /// <summary>
    /// Errors and notifications handler
    /// </summary>
    public Action<NatsJSNotification>? ErrorHandler { get; init; }

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

    /// <summary>
    /// Serializer to use to deserialize the message if a model is being used.
    /// </summary>
    /// <remarks>
    /// If not set, serializer set in connection options or the default JSON serializer
    /// will be used.
    /// </remarks>
    public INatsSerializer? Serializer { get; init; }
}
