namespace NATS.Client.Core;

/// <summary>
/// Callbacks invoked at points in a subscription's lifecycle.
/// Set using <see cref="NatsSubOpts.Events"/>.
/// </summary>
public record NatsSubEvents
{
    /// <summary>
    /// Invoked when the subscription has been established, that is, once the SUB protocol
    /// message has been queued for sending to the server. This is the same guarantee
    /// <see cref="INatsConnection.SubscribeCoreAsync{T}"/> provides when it completes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The async enumerable returned by <see cref="INatsClient.SubscribeAsync{T}"/> does not
    /// establish the subscription until it is iterated. When the enumerable is handed off to
    /// another task, use this callback (for example, to complete a
    /// <see cref="TaskCompletionSource{TResult}"/>) to find out when it is safe to trigger
    /// messages the subscription must observe. Publishers using other connections may still
    /// race with the server processing the subscription; a <see cref="INatsClient.PingAsync"/>
    /// round-trip on the subscribing connection after this callback removes that race.
    /// </para>
    /// <para>
    /// The callback receives the subscription and is invoked once when the subscription is
    /// first established; it is not invoked again when subscriptions are re-established
    /// after a reconnect. If the callback throws, the subscription is disposed and the
    /// exception propagates to the subscribe call.
    /// </para>
    /// </remarks>
    public Func<NatsSubBase, ValueTask>? OnSubscribed { get; init; }
}
