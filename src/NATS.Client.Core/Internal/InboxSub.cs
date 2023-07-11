using System.Buffers;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, INatsSub> _writers = new();

    public InboxSubBuilder(ILogger<InboxSubBuilder> logger) => _logger = logger;

    public InboxSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager, CancellationToken cancellationToken)
    {
        return new InboxSub(this, subject, opts, connection, manager);
    }

    public void Register(INatsSub sub)
    {
        if (!_writers.TryAdd(sub.Subject, sub))
            throw new InvalidOperationException("Subject already registered");
        sub.Ready();
    }

    public void Unregister(string subject) => _writers.TryRemove(subject, out _);

    public ValueTask ReceivedAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        if (!_writers.TryGetValue(subject, out var sub))
        {
            _logger.LogWarning("Unregistered message inbox received");
            return ValueTask.CompletedTask;
        }

        return sub.ReceiveAsync(subject, replyTo, headersBuffer, payloadBuffer);
    }

    public ValueTask RemoveAsync(INatsSub sub)
    {
        Unregister(sub.Subject);
        return ValueTask.CompletedTask;
    }
}
