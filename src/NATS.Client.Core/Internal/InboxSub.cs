using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

internal class InboxSub : NatsSubBase
{
    private readonly InboxSubBuilder _inbox;
    private readonly NatsConnection _connection;

    public InboxSub(
        InboxSubBuilder inbox,
        string subject,
        NatsSubOpts? opts,
        NatsConnection connection,
        INatsSubscriptionManager manager)
    : base(connection, manager, subject, queueGroup: default, opts)
    {
        _inbox = inbox;
        _connection = connection;
    }

    // Avoid base class error handling since inboxed subscribers will be responsible for that.
    public override ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer) =>
        _inbox.ReceivedAsync(subject, replyTo, headersBuffer, payloadBuffer, _connection);

    // Not used. Dummy implementation to keep base happy.
    protected override ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
        => default;

    protected override void TryComplete()
    {
    }
}

internal class InboxSubBuilder : INatsSubscriptionManager
{
    private readonly ILogger<InboxSubBuilder> _logger;
#if NETSTANDARD2_0
    private readonly ConcurrentDictionary<string, List<WeakReference<NatsSubBase>>> _bySubject = new();
#else
    private readonly ConcurrentDictionary<string, ConditionalWeakTable<NatsSubBase, object>> _bySubject = new();
#endif

    public InboxSubBuilder(ILogger<InboxSubBuilder> logger) => _logger = logger;

    public InboxSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, INatsSubscriptionManager manager)
    {
        return new InboxSub(this, subject, opts, connection, manager);
    }

    public ValueTask RegisterAsync(NatsSubBase sub)
    {
#if NETSTANDARD2_0
        _bySubject.AddOrUpdate(
            sub.Subject,
            _ =>
            {
                return new List<WeakReference<NatsSubBase>> { new WeakReference<NatsSubBase>(sub) };
            },
            (_, subTable) =>
            {
                lock (subTable)
                {
                    if (subTable.Count == 0)
                    {
                        subTable.Add(new WeakReference<NatsSubBase>(sub));
                        return subTable;
                    }

                    var wr = subTable.FirstOrDefault(w =>
                    {
                        if (w.TryGetTarget(out var t))
                        {
                            if (t == sub)
                            {
                                return true;
                            }
                        }

                        return false;
                    });

                    if (wr != null)
                    {
                        subTable.Remove(wr);
                    }

                    subTable.Add(new WeakReference<NatsSubBase>(sub));
                    return subTable;
                }
            });
#else
        _bySubject.AddOrUpdate(
                sub.Subject,
                static (_, s) => new ConditionalWeakTable<NatsSubBase, object> { { s, new object() } },
                static (_, subTable, s) =>
                {
                    lock (subTable)
                    {
                        if (!subTable.Any())
                        {
                            // if current subTable is empty, it may be in process of being removed
                            // return a new object
                            return new ConditionalWeakTable<NatsSubBase, object> { { s, new object() } };
                        }

                        // the updateValueFactory delegate can be called multiple times
                        // use AddOrUpdate to avoid exceptions if this happens
                        subTable.AddOrUpdate(s, new object());
                        return subTable;
                    }
                },
                sub);
#endif

        return sub.ReadyAsync();
    }

    public async ValueTask ReceivedAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        if (!_bySubject.TryGetValue(subject, out var subTable))
        {
            _logger.LogWarning(NatsLogEvents.InboxSubscription, "Unregistered message inbox received for {Subject}", subject);
            return;
        }

#if NETSTANDARD2_0
        foreach (var weakReference in subTable)
        {
            if (weakReference.TryGetTarget(out var sub))
            {
                await sub.ReceiveAsync(subject, replyTo, headersBuffer, payloadBuffer).ConfigureAwait(false);
            }
        }
#else
        foreach (var (sub, _) in subTable)
        {
            await sub.ReceiveAsync(subject, replyTo, headersBuffer, payloadBuffer).ConfigureAwait(false);
        }
#endif
    }

    public ValueTask RemoveAsync(NatsSubBase sub)
    {
        if (!_bySubject.TryGetValue(sub.Subject, out var subTable))
        {
            _logger.LogWarning(NatsLogEvents.InboxSubscription, "Unregistered message inbox received for {Subject}", sub.Subject);
            return default;
        }

        lock (subTable)
        {
#if NETSTANDARD2_0
            if (subTable.Count == 0)
            {
                _bySubject.TryRemove(sub.Subject, out _);
            }
#else
            if (!subTable.Remove(sub))
                _logger.LogWarning(NatsLogEvents.InboxSubscription, "Unregistered message inbox received for {Subject}", sub.Subject);

            if (!subTable.Any())
            {
                // try to remove this specific instance of the subTable
                // if an update is in process and sees an empty subTable, it will set a new instance
#if NETSTANDARD
                _bySubject.TryRemove(sub.Subject, out _);
#else
                _bySubject.TryRemove(KeyValuePair.Create(sub.Subject, subTable));
#endif
            }
#endif
        }

        return default;
    }
}
