namespace NATS.Client.Core;

public record NatsPublishProps : NatsMessagingProps
{
    public NatsPublishProps(string subject)
        : base(subject)
    {
    }
}
