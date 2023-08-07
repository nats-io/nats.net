using System.Buffers;

namespace NATS.Client.Core;

public interface INatsConnection
{
    /// <summary>
    /// Send PING command and await PONG. Return value is similar as Round Trip Time (RTT).
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous round trip operation.</returns>
    ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="payload">The message payload data.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync(in NatsMsg msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a serializable message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="subject">The destination subject to publish to.</param>
    /// <param name="data">Serializable data object</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be send to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync<T>(string subject, T data, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a serializable message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg{T}"/> representing message details.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be send to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync<T>(in NatsMsg<T> msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a subscription to a subject, optionally joining a distributed queue group.
    /// </summary>
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="opts">A <see cref="NatsSubOpts"/> for subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous send operation.</returns>
    ValueTask<INatsSub> SubscribeAsync(string subject, NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a subscription to a subject, optionally joining a distributed queue group.
    /// </summary>
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="opts">A <see cref="NatsSubOpts"/> for subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be received from the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous send operation.</returns>
    ValueTask<INatsSub<T>> SubscribeAsync<T>(string subject, NatsSubOpts? opts = default, CancellationToken cancellationToken = default);
}
