using System.Buffers;
using System.Collections.Concurrent;

namespace NATS.Client.Core;

internal interface INatsSub : IAsyncDisposable
{
    string Subject { get; }

    string? QueueGroup { get; }

    int Sid { get; }

    ValueTask ReceiveAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer);
}

internal interface INatsSubBuilder<out T>
    where T : INatsSub
{
    T Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager);
}

internal class NatsSubBuilder : INatsSubBuilder<NatsSub>
{
    public static readonly NatsSubBuilder Default = new();

    public NatsSub Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new NatsSub(connection, manager, subject, queueGroup, sid);
    }
}

internal class NatsSubModelBuilder<T> : INatsSubBuilder<NatsSub<T>>
{
    private static readonly ConcurrentDictionary<INatsSerializer, NatsSubModelBuilder<T>> Cache = new();
    private readonly INatsSerializer _serializer;

    public NatsSubModelBuilder(INatsSerializer serializer) => _serializer = serializer;

    public static NatsSubModelBuilder<T> For(INatsSerializer serializer) =>
        Cache.GetOrAdd(serializer, static s => new NatsSubModelBuilder<T>(s));

    public NatsSub<T> Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new NatsSub<T>(connection, manager, subject, queueGroup, sid, _serializer);
    }
}
