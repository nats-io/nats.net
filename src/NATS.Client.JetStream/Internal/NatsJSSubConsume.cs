using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace NATS.Client.JetStream.Internal;

/*
 *  Channel Connections
 *  -------------------
 *
 *  - Sub CH:
 *      NatsJSSub message channel where all the inbox messages are
 *      delivered to.
 *
 *  - User Messages CH:
 *      These are all the user messages (i.e. subject != inbox)
 *
 *  - User Notifications CH:
 *      Anything we want to let user know about the state of the
 *      consumer, connection status, timeouts etc.
 *
 *  The main idea is to deliver user and control messages from the server
 *  inbox subscription and internal control messages (e.g. heartbeat
 *  timeouts) to a single 'controller' where all messages would be
 *  processed in order and state managed in one place in a non-concurrent
 *  manner so that races are avoided and it's easier to reason with state
 *  changes.
 *
 *  User Notifications also have their own channel so they can be
 *  prioritized and can run in their own Task where User error handler
 *  will be dispatched.
 *
 *
 *    NATS-SERVER
 *        |                                              User
 *        |             +--> [User Messages CH] -------> message loop
 *        v           /                                  (await foreach)
 *    [Sub CH] ---> Controller (with state)
 *        ^           \                                  User error
 *        |            +--> [User Notifications CH] ---> handler
 *        |                                              (Action<>)
 *        | Internal control msgs
 *        | (e.g. heartbeat timeout)
 *        |
 *  Heartbeat Timer
 *
 */
internal class NatsJSSubConsume<T> : NatsJSSubBase<T>, INatsJSSubConsume<T>
{
    private readonly Action<NatsJSNotification>? _errorHandler;
    private readonly CancellationToken _cancellationToken;
    private readonly Task _notifier;
    private readonly Channel<NatsJSNotification> _notificationChannel;
    private readonly Channel<NatsJSMsg<T?>> _userMessageChannel;

    internal NatsJSSubConsume(
        string stream,
        string consumer,
        NatsJSContext context,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts,
        NatsJSSubState state,
        INatsSerializer serializer,
        Action<NatsJSNotification>? errorHandler = default,
        CancellationToken cancellationToken = default)
        : base(stream, consumer, context, manager, subject, opts, state, serializer, cancellationToken)
    {
        _errorHandler = errorHandler;
        _cancellationToken = cancellationToken;

        // User messages are buffered here separately to allow smoother flow while control loop
        // pulls more data in the background. This also allows control messages to be dealt with
        // in the same loop as the control messages to keep state updates consistent. This is as
        // opposed to having a control and a message channel at the point of serializing the messages
        // in NatsJSSub class.
        _userMessageChannel = Channel.CreateBounded<NatsJSMsg<T?>>(NatsSub.GetChannelOptions(opts?.ChannelOptions));

        // We drop the old message if notification handler isn't able to keep up.
        // This is to avoid blocking the control loop and making sure we deliver all the messages.
        // Assuming newer messages would be more relevant and worth keeping than older ones.
        _notificationChannel = Channel.CreateBounded<NatsJSNotification>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
        _notifier = Task.Run(NotificationLoop);
    }

    public ChannelReader<NatsJSMsg<T?>> Msgs => _userMessageChannel.Reader;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _notifier;
    }

    protected override void HeartbeatTimerCallback() =>
        _notificationChannel.Writer.WriteAsync(new NatsJSNotification(-1, "Heartbeat timeout"), _cancellationToken);

    protected override ValueTask ReceivedControlMsg(NatsJSNotification notification)
    {
        return _notificationChannel.Writer.WriteAsync(notification, _cancellationToken);
    }

    protected override ValueTask ReceivedUserMsg(NatsMsg<T?> msg)
    {
        return _userMessageChannel.Writer.WriteAsync(new NatsJSMsg<T?>(msg), _cancellationToken);
    }

    protected override void TryComplete()
    {
        _userMessageChannel.Writer.Complete();
        _notificationChannel.Writer.Complete();
    }

    private async Task NotificationLoop()
    {
        await foreach (var notification in _notificationChannel.Reader.ReadAllAsync(_cancellationToken))
        {
            try
            {
                _errorHandler?.Invoke(notification);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "User notification callback error");
            }
        }
    }
}
