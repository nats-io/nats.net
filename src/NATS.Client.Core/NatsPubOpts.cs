namespace NATS.Client.Core;

public record NatsPubOpts
{
    /// <summary>
    /// Obsolete option historically used to control when PublishAsync returned
    /// No longer has any effect
    /// This option method will be removed in a future release
    /// </summary>
    [Obsolete("No longer has any effect")]
    public bool? WaitUntilSent { get; init; }

    /// <summary>
    /// Obsolete callback historically used for handling serialization errors
    /// All errors are now thrown as exceptions in PublishAsync
    /// This option method will be removed in a future release
    /// </summary>
    [Obsolete("All errors are now thrown as exceptions in PublishAsync")]
    public Action<Exception>? ErrorHandler { get; init; }
}
