namespace NATS.Client.Core;

/// <summary>
/// Properties which can be used to perform an operation when connected to a NATS server.
/// </summary>
/// <remarks>This is commonly implemented as <see cref="NatsPublishProps"/>, <see cref="NatsSubscribeProps"/> and <see cref="NatsUnsubscribeProps"/></remarks>
public abstract record NatsOperationProps
{
    /// <summary>
    /// Create a new NATS Operation based using the specified <paramref name="subject"/>.
    /// </summary>
    /// <param name="subject">The Subject used for the NATS operation</param>
    internal NatsOperationProps(string subject)
    {
        Subject = subject;
    }

    /// <summary>
    /// The Subject used for the NATS operation
    /// </summary>
    public string Subject { get; private set; }

    internal bool IsInboxSubject(string inboxPrefix) => !string.IsNullOrEmpty(inboxPrefix)
        && Subject.StartsWith(inboxPrefix, StringComparison.Ordinal);
}
