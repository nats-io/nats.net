using System.Buffers;
using System.Threading.Channels;

namespace NATS.Client.Core;

public abstract class NatsSubBase : IDisposable
{
    internal NatsSubBase(NatsConnection connection, SubscriptionManager manager, string subject, string? queueGroup, int sid)
    {
        Connection = connection;
        Manager = manager;
        Subject = subject;
        QueueGroup = queueGroup;
        Sid = sid;
    }

    public string Subject { get; }

    public string? QueueGroup { get; }

    internal int Sid { get; }

    internal NatsConnection Connection { get; }

    internal SubscriptionManager Manager { get; }

    public virtual void Dispose()
    {
        Manager.Remove(Sid);
    }

    internal abstract ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte> buffer);
}

public sealed class NatsSub : NatsSubBase
{
    private readonly Channel<NatsMsg> _msgs = Channel.CreateBounded<NatsMsg>(new BoundedChannelOptions(1_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = true,
        SingleReader = false,
        AllowSynchronousContinuations = false,
    });

    internal NatsSub(NatsConnection connection, SubscriptionManager manager, string subject, string? queueGroup, int sid)
        : base(connection, manager, subject, queueGroup, sid)
    {
    }

    public ChannelReader<NatsMsg> Msgs => _msgs.Reader;

    public override void Dispose()
    {
        _msgs.Writer.TryComplete();
        base.Dispose();
    }

    internal override ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte> buffer)
    {
        return _msgs.Writer.WriteAsync(new NatsMsg(subject, buffer.ToArray())
        {
            Connection = Connection,
            ReplyTo = replyTo,
        });
    }
}

public sealed class NatsSub<T> : NatsSubBase
{
    private readonly Channel<NatsMsg<T>> _msgs = Channel.CreateBounded<NatsMsg<T>>(new BoundedChannelOptions(capacity: 1_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = true,
        SingleReader = false,
        AllowSynchronousContinuations = false,
    });

    internal NatsSub(NatsConnection connection, SubscriptionManager manager, string subject, string? queueGroup, int sid, INatsSerializer serializer)
        : base(connection, manager, subject, queueGroup, sid) => Serializer = serializer;

    public ChannelReader<NatsMsg<T>> Msgs => _msgs.Reader;

    private INatsSerializer Serializer { get; }

    public override void Dispose()
    {
        _msgs.Writer.TryComplete();
        base.Dispose();
    }

    internal override ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte> buffer)
    {
        var serializer = Serializer;
        var data = serializer.Deserialize<T>(buffer);
        return _msgs.Writer.WriteAsync(new NatsMsg<T>(subject, data!)
        {
            Connection = Connection,
            ReplyTo = replyTo,
        });
    }
}
