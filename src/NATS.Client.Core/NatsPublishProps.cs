namespace NATS.Client.Core;

/// <remarks></remarks>
/// <inheritdoc />
public record NatsPublishProps : NatsMessagingProps
{
    /// <param name="subject">The destination subject to publish to.</param>
    /// <inheritdoc />
    public NatsPublishProps(string subject)
        : base(subject)
    {
    }
}
