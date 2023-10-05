using System.Runtime.CompilerServices;
using NATS.Client.Core.Tests;

namespace NATS.Client.TestUtilities;

public class SubscriberSync
{
    private readonly string _syncSubject;
    private readonly NatsConnection _nats;
    private readonly TimeSpan _timeout;
    private int _sync;

    public SubscriberSync(string syncSubject, NatsConnection nats, TimeSpan timeout)
    {
        _syncSubject = syncSubject;
        _nats = nats;
        _timeout = timeout;
    }

    public SubscriberSync(string syncSubject, NatsConnection nats)
    {
        _syncSubject = syncSubject;
        _nats = nats;
        _timeout = TimeSpan.FromSeconds(10);
    }

    public bool TrySet(string msgSubject)
    {
        if (msgSubject != _syncSubject)
            return false;

        Interlocked.Increment(ref _sync);
        return true;
    }

    public TaskAwaiter GetAwaiter() =>
        Retry.Until(
                reason: $"subscribed {_syncSubject}",
                condition: () => Volatile.Read(ref _sync) > 0,
                action: async () => await _nats.PublishAsync(_syncSubject).ConfigureAwait(false))
            .WaitAsync(_timeout)
            .GetAwaiter();
}
