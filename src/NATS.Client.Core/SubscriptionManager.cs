using System.Buffers;
using System.Collections.Concurrent;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

// TODO: Clean-up subscription management
// * Remove internal subscription to same subject
// * Double check if we need a weak reference SubscriptionManager - Subscription link.
//   This is to avoid a leak if user was to not dispose the subscription and GC won't collect
//   it since manager might still hold a reference to the subscription.
internal sealed class SubscriptionManager : IDisposable
{
#pragma warning disable SA1401
    internal readonly object Gate = new object(); // lock for add/remove, publish can avoid lock.
    internal readonly NatsConnection Connection;
#pragma warning restore SA1401

    private readonly ConcurrentDictionary<int, SubscriptionRef> _bySubscriptionId = new();

    private int _subscriptionId = 0; // unique alphanumeric subscription ID, generated by the client(per connection).

    public SubscriptionManager(NatsConnection connection)
    {
        Connection = connection;
    }

    public (int subscriptionId, string subject, string? queueGroup)[] GetExistingSubscriptions()
    {
        lock (Gate)
        {
            return _bySubscriptionId.Select(x => (x.Value.SubscriptionId, Key: x.Value.Subject, x.Value.QueueGroup)).ToArray();
        }
    }

    public async ValueTask<IDisposable> AddAsync<T>(string subject, string? queueGroup, object handler, CancellationToken cancellationToken)
    {
        int sid;
        SubscriptionRef? subscription;
        int handlerId;

        lock (Gate)
        {
            sid = Interlocked.Increment(ref _subscriptionId);

            subscription = new SubscriptionRef(this, sid, subject, queueGroup);
            handlerId = subscription.AddHandler(handler);
            _bySubscriptionId[sid] = subscription;
        }

        var returnSubscription = new InternalSubscription(subscription, handlerId);
        try
        {
            await Connection.SubscribeCoreAsync(sid, subject, queueGroup, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            returnSubscription.Dispose(); // can't subscribed, remove from holder.
            throw;
        }

        return returnSubscription;
    }

    public Task PublishToClientHandlersAsync(string subject, string? replyTo, int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        SubscriptionRef? subscription;
        object?[] list;
        lock (Gate)
        {
            if (_bySubscriptionId.TryGetValue(subscriptionId, out subscription))
            {
                list = subscription.Handlers.GetValues();
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        return MessagePublisher.PublishAsync(subject, replyTo, Connection.Options, buffer, list);
    }

    public void Dispose()
    {
        lock (Gate)
        {
            // remove all references.
            foreach (var item in _bySubscriptionId)
            {
                item.Value.Handlers.Dispose();
            }

            _bySubscriptionId.Clear();
        }
    }

    internal void Remove(string key, int subscriptionId)
    {
        // inside lock from RefCountSubscription.RemoveHandler
        _bySubscriptionId.Remove(subscriptionId, out _);
    }

    private sealed class InternalSubscription : IDisposable
    {
        private readonly SubscriptionRef _rootSubscription;
        private readonly int _handlerId;
        private volatile bool _isDisposed;

        public InternalSubscription(SubscriptionRef rootSubscription, int handlerId)
        {
            _rootSubscription = rootSubscription;
            _handlerId = handlerId;
        }

        ~InternalSubscription() => ReleaseResources();

        public void Dispose()
        {
            ReleaseResources();
            GC.SuppressFinalize(this);
        }

        private void ReleaseResources()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _rootSubscription.RemoveHandler(_handlerId);
        }
    }
}

internal sealed class SubscriptionRef
{
    private readonly SubscriptionManager _manager;

    public SubscriptionRef(SubscriptionManager manager, int subscriptionId, string subject, string? queueGroup)
    {
        _manager = manager;
        SubscriptionId = subscriptionId;
        Subject = subject;
        QueueGroup = queueGroup;
        Handlers = new FreeList<object>();
    }

    public int SubscriptionId { get; }

    public string Subject { get; }

    public string? QueueGroup { get; }

    // TODO: Subscription manager - single handle / nats sub object
    public FreeList<object> Handlers { get; }

    // Add is in lock(gate)
    public int AddHandler(object handler)
    {
        if (handler is NatsSubBase natsSub)
        {
            natsSub.Sid = SubscriptionId;
        }

        var id = Handlers.Add(handler);
        Interlocked.Increment(ref _manager.Connection.Counter.SubscriptionCount);
        return id;
    }

    public void RemoveHandler(int handlerId)
    {
        lock (_manager.Gate)
        {
            Handlers.Remove(handlerId, false);
            Interlocked.Decrement(ref _manager.Connection.Counter.SubscriptionCount);
            _manager.Remove(Subject, SubscriptionId);
            Handlers.Dispose();
            _manager.Connection.PostUnsubscribe(SubscriptionId);
        }
    }
}
