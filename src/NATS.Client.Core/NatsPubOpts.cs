namespace NATS.Client.Core;

public record NatsPubOpts
{
    public INatsSerializer? Serializer { get; init; }

    /// <summary>
    /// When set to true, calls to PublishAsync will complete after data has been written to socket
    /// Default value is false, and calls to PublishAsync will complete after the publish command has been written to the Command Channel
    /// </summary>
    public bool? WaitUntilSent { get; init; }
}
