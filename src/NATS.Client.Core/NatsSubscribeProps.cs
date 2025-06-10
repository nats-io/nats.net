namespace NATS.Client.Core;

/// <remarks>This is used to either subscribe or unsubscribe to a subject</remarks>
/// <inheritdoc/>
public record NatsSubscribeProps : NatsUnsubscribeProps
{
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="queueGroup">If specified, the subscriber will join this queue group.</param>
    /// <inheritdoc/>
    public NatsSubscribeProps(string subject, string? queueGroup = default)
        : base(subject)
    {
        QueueGroup = queueGroup;
    }

    /// <summary>
    /// The client will join this queue group which helps to enable high availability clients
    /// </summary>
    public string? QueueGroup { get; internal set; }
}
