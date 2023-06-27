using System.Buffers;
using System.Collections.Concurrent;

namespace NATS.Client.Core;

public interface INatsSub : IAsyncDisposable
{
    string Subject { get; }

    string? QueueGroup { get; }

    int Sid { get; }

    ValueTask ReceiveAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer);
}

public interface INatsSubBuilder<out T>
    where T : INatsSub
{
    T Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager);
}

public class NatsSubBuilder : INatsSubBuilder<NatsSub>
{
    public static NatsSubBuilder Default = new();

    public NatsSub Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new NatsSub(connection, manager, subject, queueGroup, sid);
    }
}

public class NatsSubModelBuilder<T> : INatsSubBuilder<NatsSub<T>>
{
    private static readonly ConcurrentDictionary<INatsSerializer, NatsSubModelBuilder<T>> Cache = new();
    private readonly INatsSerializer _serializer;

    public static NatsSubModelBuilder<T> For(INatsSerializer serializer) =>
        Cache.GetOrAdd(serializer, static s => new NatsSubModelBuilder<T>(s));

    public NatsSubModelBuilder(INatsSerializer serializer) => _serializer = serializer;

    public NatsSub<T> Build(string subject, string? queueGroup, NatsConnection connection, SubscriptionManager manager)
    {
        var sid = manager.GetNextSid();
        return new NatsSub<T>(connection, manager, subject, queueGroup, sid, _serializer);
    }
}
