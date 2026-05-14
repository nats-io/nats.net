using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace NATS.Client.Core;

public interface INatsConnection : INatsClient
{
    /// <summary>
    /// Event that is raised when the connection to the NATS server is disconnected.
    /// </summary>
    event AsyncEventHandler<NatsEventArgs>? ConnectionDisconnected;

    /// <summary>
    /// Event that is raised when the connection to the NATS server is opened.
    /// </summary>
    event AsyncEventHandler<NatsEventArgs>? ConnectionOpened;

    /// <summary>
    /// Event that is raised when a reconnect attempt is failed.
    /// </summary>
    event AsyncEventHandler<NatsEventArgs>? ReconnectFailed;

    /// <summary>
    /// Event that is raised when a message is dropped for a subscription.
    /// </summary>
    event AsyncEventHandler<NatsMessageDroppedEventArgs>? MessageDropped;

    /// <summary>
    /// Event that is raised when a slow consumer is detected on a subscription.
    /// This event fires once per "episode" - when the subscription transitions into a slow consumer state.
    /// It will fire again if the subscription recovers (channel drains to nearly empty) and then becomes slow again.
    /// </summary>
    event AsyncEventHandler<NatsSlowConsumerEventArgs>? SlowConsumerDetected;

    /// <summary>
    /// Event that is raised when server goes into Lame Duck Mode.
    /// </summary>
    public event AsyncEventHandler<NatsLameDuckModeActivatedEventArgs>? LameDuckModeActivated;

    /// <summary>
    /// Server information received from the NATS server.
    /// </summary>
    INatsServerInfo? ServerInfo { get; }

    /// <summary>
    /// Options used to configure the NATS connection.
    /// </summary>
    NatsOpts Opts { get; }

    /// <summary>
    /// Connection state of the NATS connection.
    /// </summary>
    NatsConnectionState ConnectionState { get; }

    /// <summary>
    /// Subscription manager used to manage subscriptions for the NATS connection.
    /// </summary>
    INatsSubscriptionManager SubscriptionManager { get; }

    /// <summary>
    /// Singleton instance of the NATS header parser used to parse message headers
    /// used by the NATS connection.
    /// </summary>
    NatsHeaderParser HeaderParser { get; }

    /// <summary>
    /// Hook before TCP connection open.
    /// </summary>
    Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync { get; set; }

    /// <summary>
    /// Hook when socket is available.
    /// </summary>
    Func<INatsSocketConnection, ValueTask<INatsSocketConnection>>? OnSocketAvailableAsync { get; set; }

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
    /// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.Core.Extensions">RequestManyWithSentinelAsync for custom stop conditions</seealso>
    IAsyncEnumerable<NatsMsg<TReply>> RequestManyAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a subscription to the NATS connection for a given <see cref="NatsSubBase"/> object.
    /// Subscriptions are managed by the connection and are automatically removed when the connection is closed.
    /// </summary>
    /// <param name="sub">The <see cref="NatsSubBase"/> object representing the subscription details.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous subscription operation.</returns>
    ValueTask AddSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription with appropriate request and reply subjects publishing the request.
    /// It's the caller's responsibility to retrieve the reply messages and complete the subscription.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data.</typeparam>
    /// <typeparam name="TReply">The type of the expected reply.</typeparam>
    /// <param name="subject">The subject to subscribe to.</param>
    /// <param name="data">The optional request data.</param>
    /// <param name="headers">The optional headers to include with the request.</param>
    /// <param name="requestSerializer">The optional serializer for the request data.</param>
    /// <param name="replySerializer">The optional deserializer for the reply data.</param>
    /// <param name="requestOpts">The optional publishing options for the request.</param>
    /// <param name="replyOpts">The optional subscription options for the reply.</param>
    /// <param name="cancellationToken">The optional cancellation token.</param>
    /// <returns>A <see cref="ValueTask{T}"/> representing the asynchronous operation of creating the request subscription.</returns>
    ValueTask<NatsSub<TReply>> CreateRequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the bounded channel options for creating a channel used by a subscription.
    /// Options are built from the connection's configuration and the subscription channel options.
    /// Used to aid in custom message handling when building a subscription channel.
    /// </summary>
    /// <param name="subChannelOpts">The options for configuring the subscription channel.</param>
    /// <returns>The bounded channel options used for creating the subscription channel.</returns>
    BoundedChannelOptions GetBoundedChannelOpts(NatsSubChannelOpts? subChannelOpts);

    /// <summary>
    /// Called when a message is dropped for a subscription.
    /// Used to aid in custom message handling when a subscription's message channel is full.
    /// </summary>
    /// <param name="natsSub">The <see cref="NatsSubBase"/> representing the subscription.</param>
    /// <param name="pending">The number of pending messages at the time the drop occurred.</param>
    /// <param name="msg">The dropped message represented by <see cref="NatsMsg{T}"/>.</param>
    /// <typeparam name="T">Specifies the type of data in the dropped message.</typeparam>
    /// <remarks>
    /// This method is expected to complete quickly to avoid further delays in processing;
    /// if complex work is required, it is recommended to offload to a channel or other out-of-band processor.
    /// </remarks>
    void OnMessageDropped<T>(NatsSubBase natsSub, int pending, NatsMsg<T> msg);
}
