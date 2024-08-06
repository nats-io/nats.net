using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace NATS.Client.Core;

public interface INatsConnection : INatsClient
{
    event AsyncEventHandler<NatsEventArgs>? ConnectionDisconnected;

    event AsyncEventHandler<NatsEventArgs>? ConnectionOpened;

    event AsyncEventHandler<NatsEventArgs>? ReconnectFailed;

    event AsyncEventHandler<NatsMessageDroppedEventArgs>? MessageDropped;

    INatsServerInfo? ServerInfo { get; }

    NatsOpts Opts { get; }

    NatsConnectionState ConnectionState { get; }

    INatsSubscriptionManager SubscriptionManager { get; }

    NatsHeaderParser HeaderParser { get; }

    /// <summary>
    /// Publishes a serializable message payload to the given subject name, optionally supplying a reply subject.
    /// </summary>
    /// <param name="msg">A <see cref="NatsMsg{T}"/> representing message details.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsPubOpts"/> for publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be sent to the NATS Server.</typeparam>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous send operation.</returns>
    ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a subscription to a subject, optionally joining a distributed queue group
    /// and returns a <see cref="INatsSub{T}"/> object which provides more control over the subscription.
    /// </summary>
    /// <param name="subject">The subject name to subscribe to.</param>
    /// <param name="queueGroup">If specified, the subscriber will join this queue group.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">A <see cref="NatsSubOpts"/> for subscription options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the command.</param>
    /// <typeparam name="T">Specifies the type of data that may be received from the NATS Server.</typeparam>
    /// <returns>An asynchronous task that completes with the NATS subscription.</returns>
    /// <remarks>
    /// <para>
    /// Subscribers with the same queue group name, become a queue group,
    /// and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </para>
    /// <para>
    /// This method returns a <see cref="INatsSub{T}"/> object which provides slightly lower level
    /// control over the subscription. You can use this object to create your own core messaging
    /// patterns or to create your own higher level abstractions.
    /// </para>
    /// </remarks>
    ValueTask<INatsSub<T>> SubscribeCoreAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new inbox subject with the form {Inbox Prefix}.{Unique Connection ID}.{Unique Inbox ID}
    /// </summary>
    /// <returns>A <see cref="string"/> containing a unique inbox subject.</returns>
    string NewInbox();

    /// <summary>
    /// Request and receive zero or more replies from a responder.
    /// </summary>
    /// <param name="subject">Subject of the responder</param>
    /// <param name="data">Data to send to responder</param>
    /// <param name="headers">Optional message headers</param>
    /// <param name="requestSerializer">Serializer to use for the request message type.</param>
    /// <param name="replySerializer">Serializer to use for the reply message type.</param>
    /// <param name="requestOpts">Request publish options</param>
    /// <param name="replyOpts">Reply handler subscription options</param>
    /// <param name="cancellationToken">Cancel this request</param>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TReply">Reply type</typeparam>
    /// <returns>An asynchronous enumerable of <see cref="NatsMsg{T}"/> objects</returns>
    /// <exception cref="OperationCanceledException">Raised when cancellation token is used</exception>
    /// <remarks>
    /// if reply option's timeout is not defined then it will be set to NatsOpts.RequestTimeout.
    /// </remarks>
    IAsyncEnumerable<NatsMsg<TReply>> RequestManyAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    void OnMessageDropped<T>(NatsSubBase natsSub, int pending, NatsMsg<T> msg);

    ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default);

    BoundedChannelOptions GetChannelOpts(NatsOpts connectionOpts, NatsSubChannelOpts? subChannelOpts);

    ValueTask<NatsSub<TReply>> RequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);
}
