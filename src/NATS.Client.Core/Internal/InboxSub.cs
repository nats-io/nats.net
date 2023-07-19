using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

internal class InboxSub : INatsSub
{
    private readonly InboxSubBuilder _inbox;
    private readonly NatsConnection _connection;
    private readonly ISubscriptionManager _manager;

    public InboxSub(
        InboxSubBuilder inbox,
        string subject,
        NatsSubOpts? opts,
        NatsConnection connection,
        ISubscriptionManager manager)
    {
        _inbox = inbox;
        _connection = connection;
        _manager = manager;
        Subject = subject;
        QueueGroup = opts?.QueueGroup;
        PendingMsgs = opts?.MaxMsgs;
    }

    public string Subject { get; }

    public string? QueueGroup { get; }

    public int? PendingMsgs { get; }

    public void Ready()
    {
    }

    public ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        return _inbox.ReceivedAsync(subject, replyTo, headersBuffer, payloadBuffer, _connection);
    }

    public ValueTask DisposeAsync() => _manager.RemoveAsync(this);
}

internal class InboxSubBuilder : INatsSubBuilder<InboxSub>, ISubscriptionManager
{
    private readonly ILogger<InboxSubBuilder> _logger;
    private readonly ConcurrentDictionary<string, ConditionalWeakTable<INatsSub, object>> _bySubject = new();

    public InboxSubBuilder(ILogger<InboxSubBuilder> logger) => _logger = logger;

    public InboxSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager, CancellationToken cancellationToken)
    {
        return new InboxSub(this, subject, opts, connection, manager);
    }

    public void Register(INatsSub sub)
    {
        _bySubject.AddOrUpdate(
                sub.Subject,
                _ => new ConditionalWeakTable<INatsSub, object> { { sub, new object() } },
                (_, subTable) =>
                {
                    lock (subTable)
                    {
                        if (!subTable.Any())
                        {
                            // if current subTable is empty, it may be in process of being removed
                            // return a new object
                            return new ConditionalWeakTable<INatsSub, object> { { sub, new object() } };
                        }

                        // the updateValueFactory delegate can be called multiple times
                        // use AddOrUpdate to avoid exceptions if this happens
                        subTable.AddOrUpdate(sub, new object());
                        return subTable;
                    }
                });

        sub.Ready();
    }

    public async ValueTask ReceivedAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        if (!_bySubject.TryGetValue(subject, out var subTable))
        {
            _logger.LogWarning($"Unregistered message inbox received for {subject}");
            return;
        }

        foreach (var (sub, _) in subTable)
        {
            await sub.ReceiveAsync(subject, replyTo, headersBuffer, payloadBuffer).ConfigureAwait(false);
        }
    }

    public ValueTask RemoveAsync(INatsSub sub)
    {
        if (!_bySubject.TryGetValue(sub.Subject, out var subTable))
        {
            _logger.LogWarning($"Unregistered message inbox received for {sub.Subject}");
            return ValueTask.CompletedTask;
        }

        lock (subTable)
        {
            if (!subTable.Remove(sub))
                _logger.LogWarning($"Unregistered message inbox received for {sub.Subject}");

            if (!subTable.Any())
            {
                // try to remove this specific instance of the subTable
                // if an update is in process and sees an empty subTable, it will set a new instance
                _bySubject.TryRemove(KeyValuePair.Create(sub.Subject, subTable));
            }
        }

        return ValueTask.CompletedTask;
    }
}
