using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

internal class InboxSub : NatsSubBase
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
    : base(connection, manager, subject, opts)
    {
        _inbox = inbox;
        _connection = connection;
        _manager = manager;
    }

    // public ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    // {
    //     return _inbox.ReceivedAsync(subject, replyTo, headersBuffer, payloadBuffer, _connection);
    // }
    protected override ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer) =>
        _inbox.ReceivedAsync(subject, replyTo, headersBuffer, payloadBuffer, _connection);

    protected override void TryComplete()
    {
    }
}

internal class InboxSubBuilder : ISubscriptionManager
{
    private readonly ILogger<InboxSubBuilder> _logger;
    private readonly ConcurrentDictionary<string, ConditionalWeakTable<NatsSubBase, object>> _bySubject = new();

    public InboxSubBuilder(ILogger<InboxSubBuilder> logger) => _logger = logger;

    public InboxSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager)
    {
        return new InboxSub(this, subject, opts, connection, manager);
    }

    public void Register(NatsSubBase sub)
    {
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

    public ValueTask RemoveAsync(NatsSubBase sub)
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
