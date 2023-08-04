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
