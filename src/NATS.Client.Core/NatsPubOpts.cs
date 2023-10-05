namespace NATS.Client.Core;

public record NatsPubOpts
{
    public INatsSerializer? Serializer { get; init; }

    /// <summary>
    /// When set to true, calls to PublishAsync will complete after data has been written to socket
    /// Default value is false, and calls to PublishAsync will complete after the publish command has been written to the Command Channel
    /// </summary>
    public bool? WaitUntilSent { get; init; }

    /// <summary>
    /// Optional callback to handle serialization exceptions.
    /// </summary>
    /// <remarks>
    /// When <c>WaitUntilSent</c> is set to <c>false</c> serialization exceptions won't propagate
    /// to the caller but this callback will be called with the exception thrown by the serializer.
    /// </remarks>
    public Action<Exception>? ErrorHandler { get; init; }
}
