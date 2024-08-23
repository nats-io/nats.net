namespace NATS.Client.Core;

/// <summary>
/// Subscription manager interface.
/// </summary>
/// <remarks>
/// This interface is used to manage subscriptions. However, it is not intended to be used directly.
/// You can implement this interface if you are using low-level APIs and implement your own
/// subscription manager.
/// </remarks>
public interface INatsSubscriptionManager
{
    /// <summary>
    /// Remove a subscription.
    /// </summary>
    /// <param name="sub">Subscription to remove.</param>
    /// <returns>A value task that represents the asynchronous remove operation.</returns>
    public ValueTask RemoveAsync(NatsSubBase sub);
}
