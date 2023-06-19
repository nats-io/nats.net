using System.Buffers;
using System.Text;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public abstract class NatsSubBase : IAsyncDisposable
{
    internal NatsSubBase(NatsConnection connection, SubscriptionManager manager, string subject, string? queueGroup, int sid)
    {
        Connection = connection;
        Manager = manager;
        HeaderParser = new HeaderParser(Encoding.UTF8);
        Subject = subject;
        QueueGroup = queueGroup;
        Sid = sid;
    }

    public string Subject { get; }

    public string? QueueGroup { get; }

    internal int Sid { get; }

    internal NatsConnection Connection { get; }

    internal SubscriptionManager Manager { get; }

    internal HeaderParser HeaderParser { get; }

    public virtual ValueTask DisposeAsync()
    {
        return Manager.RemoveAsync(Sid);
    }

    internal abstract ValueTask ReceiveAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer);
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

    public override ValueTask DisposeAsync()
    {
        _msgs.Writer.TryComplete();
        return base.DisposeAsync();
    }

    internal override ValueTask ReceiveAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer)
    {
        NatsHeaders? natsHeaders = null;
        if (headersBuffer != null)
        {
            natsHeaders = new NatsHeaders();
            if (!HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), natsHeaders))
            {
                throw new NatsException("Error parsing headers");
            }

            natsHeaders.SetReadOnly();
        }

        return _msgs.Writer.WriteAsync(new NatsMsg(subject, payloadBuffer.ToArray())
        {
            Connection = Connection,
            ReplyTo = replyTo,
            Headers = natsHeaders,
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

    public override ValueTask DisposeAsync()
    {
        _msgs.Writer.TryComplete();
        return base.DisposeAsync();
    }

    internal override ValueTask ReceiveAsync(string subject, string? replyTo, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer)
    {
        var serializer = Serializer;
        var data = serializer.Deserialize<T>(payloadBuffer);

        NatsHeaders? natsHeaders = null;
        if (headersBuffer != null)
        {
            natsHeaders = new NatsHeaders();
            if (!HeaderParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), natsHeaders))
            {
                throw new NatsException("Error parsing headers");
            }

            natsHeaders.SetReadOnly();
        }

        return _msgs.Writer.WriteAsync(new NatsMsg<T>(subject, data!)
        {
            Connection = Connection,
            ReplyTo = replyTo,
            Headers = natsHeaders,
        });
    }
}
