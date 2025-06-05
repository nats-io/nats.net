namespace NATS.Client.Core;

public record NatsOperationProps
{
    internal NatsOperationProps(string subject)
    {
        Subject = subject;
    }

    public string Subject { get; private set; }
}
