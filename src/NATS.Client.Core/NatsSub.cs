using System.Threading.Channels;

namespace NATS.Client.Core;

public class NatsSub : NatsSubBase
{
    private readonly Channel<NatsMsg> _msgs = Channel.CreateUnbounded<NatsMsg>();

    public ChannelReader<NatsMsg> Msgs => _msgs.Reader;

    public override void Dispose()
    {
        if (_msgs.Writer.TryComplete())
        {
            base.Dispose();
        }
    }

    internal ValueTask ReceiveAsync(NatsMsg msg)
    {
        return _msgs.Writer.WriteAsync(msg);
    }
}

public abstract class NatsSubBase : IDisposable
{
    public string Subject
    {
        get => SubjectKey.Key;
        internal set => SubjectKey = new NatsKey(value);
    }

    public string QueueGroup
    {
        get => SubjectKey.Key;
        internal set => SubjectKey = new NatsKey(value);
    }

    internal NatsKey SubjectKey { get; set; }

    internal NatsKey QueueGroupKey { get; set; }

    internal int Sid { get; set; }

    internal NatsConnection? Connection { get; set; }

    public virtual void Dispose()
    {
        Connection?.PostUnsubscribe(Sid);
    }
}

public class NatsSub<T> : NatsSubBase
{
    private readonly Channel<NatsMsg<T>> _msgs = Channel.CreateUnbounded<NatsMsg<T>>();

    public INatsSerializer? Serializer { get; internal set; }

    public ChannelReader<NatsMsg<T>> Msgs => _msgs.Reader;

    public override void Dispose()
    {
        if (_msgs.Writer.TryComplete())
        {
            base.Dispose();
        }
    }

    internal ValueTask ReceiveAsync(NatsMsg msg)
    {
        var serializer = Serializer ?? Connection!.Options.Serializer;
        var tData = serializer.Deserialize<T>(msg.Data);
        return _msgs.Writer.WriteAsync(new NatsMsg<T>(tData!)
        {
            Connection = msg.Connection,
            SubjectKey = msg.SubjectKey,
            ReplyToKey = msg.ReplyToKey,
            Headers = msg.Headers,
        });
    }
}
