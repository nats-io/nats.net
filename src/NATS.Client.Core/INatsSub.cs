using System.Buffers;
using System.Collections.Concurrent;

namespace NATS.Client.Core;

internal interface INatsSub : IAsyncDisposable
{
    string Subject { get; }

    string? QueueGroup { get; }

    int? PendingMsgs { get; }

    int Sid { get; }

    /// <summary>
    /// Called after subscription is sent to the server.
    /// Helps maintain more accurate timeouts, especially idle timeout.
    /// </summary>
    void Ready();

    ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer);
}

internal interface INatsSubBuilder<out T>
    where T : INatsSub
{
    T Build(string subject, NatsSubOpts? opts, NatsConnection connection, SubscriptionManager manager);
}

internal class NatsSubBuilder : INatsSubBuilder<NatsSub>
{
    public static readonly NatsSubBuilder Default = new();

    public NatsSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new NatsSub(connection, manager, subject, opts, sid);
    }
}

internal class NatsSubModelBuilder<T> : INatsSubBuilder<NatsSub<T>>
{
    private static readonly ConcurrentDictionary<INatsSerializer, NatsSubModelBuilder<T>> Cache = new();
    private readonly INatsSerializer _serializer;

    public NatsSubModelBuilder(INatsSerializer serializer) => _serializer = serializer;

    public static NatsSubModelBuilder<T> For(INatsSerializer serializer) =>
        Cache.GetOrAdd(serializer, static s => new NatsSubModelBuilder<T>(s));

    public NatsSub<T> Build(string subject, NatsSubOpts? opts, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new NatsSub<T>(connection, manager, subject, opts, sid, _serializer);
    }
}
