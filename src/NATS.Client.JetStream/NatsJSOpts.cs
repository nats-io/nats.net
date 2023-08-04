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
    /// Maximum number of messages stored in the buffer
    /// </summary>
    public Action<int>? ErrorHandler { get; init; }

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
}

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
}

internal static class NatsJSOpsDefaults
{
    private static readonly TimeSpan ExpiresDefault = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiresMin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatCap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatMin = TimeSpan.FromSeconds(.5);

    internal static (int MaxMsgs, int MaxBytes, int ThresholdMsgs, int ThresholdBytes) SetMax(
        NatsJSOpts? opts = default,
        int? maxMsgs = default,
        int? maxBytes = default,
        int? thresholdMsgs = default,
        int? thresholdBytes = default)
    {
        var jsOpts = opts ?? new NatsJSOpts();
        int maxMsgsOut;
        int maxBytesOut;

        if (maxMsgs.HasValue && maxBytes.HasValue)
        {
            throw new NatsJSException($"You can only set {nameof(maxBytes)} or {nameof(maxMsgs)}");
        }
        else if (!maxMsgs.HasValue && !maxBytes.HasValue)
        {
            maxMsgsOut = jsOpts.MaxMsgs;
            maxBytesOut = 0;
        }
        else if (maxMsgs.HasValue && !maxBytes.HasValue)
        {
            maxMsgsOut = maxMsgs.Value;
            maxBytesOut = 0;
        }
        else if (!maxMsgs.HasValue && maxBytes.HasValue)
        {
            maxMsgsOut = 1_000_000;
            maxBytesOut = maxBytes.Value;
        }
        else
        {
            throw new NatsJSException($"Invalid state: {nameof(NatsJSOpsDefaults)}: {nameof(SetMax)}");
        }

        var thresholdMsgsOut = thresholdMsgs ?? maxMsgsOut / 2;
        if (thresholdMsgsOut > maxMsgsOut)
            thresholdMsgsOut = maxMsgsOut;

        var thresholdBytesOut = thresholdBytes ?? maxBytesOut / 2;
        if (thresholdBytesOut > maxBytesOut)
            thresholdBytesOut = maxBytesOut;

        return (maxMsgsOut, maxBytesOut, thresholdMsgsOut, thresholdBytesOut);
    }

    internal static (TimeSpan Expires, TimeSpan IdleHeartbeat) SetTimeouts(
        TimeSpan? expires = default,
        TimeSpan? idleHeartbeat = default)
    {
        var expiresOut = expires ?? ExpiresDefault;
        if (expiresOut < ExpiresMin)
            expiresOut = ExpiresMin;

        var idleHeartbeatOut = idleHeartbeat ?? expiresOut / 2;
        if (idleHeartbeatOut > HeartbeatCap)
            idleHeartbeatOut = HeartbeatCap;
        if (idleHeartbeatOut < HeartbeatMin)
            idleHeartbeatOut = HeartbeatMin;

        return (expiresOut, idleHeartbeatOut);
    }
}
