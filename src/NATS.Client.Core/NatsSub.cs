using System.Buffers;
using System.Threading.Channels;

namespace NATS.Client.Core;

public abstract class NatsSubBase : IAsyncDisposable
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

    internal ValueTask<IDisposable> InternalSubscription { get; set; }

    public virtual async ValueTask DisposeAsync()
    {
        (await InternalSubscription.ConfigureAwait(false)).Dispose();
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

    public ChannelReader<NatsMsg> Msgs => _msgs.Reader;

    public override async ValueTask DisposeAsync()
    {
        if (_msgs.Writer.TryComplete())
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal override ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte> buffer)
    {
        return _msgs.Writer.WriteAsync(new NatsMsg
        {
            Connection = Connection,
            Subject = subject,
            ReplyTo = replyTo,
            Data = buffer.ToArray(),
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

    public INatsSerializer? Serializer { get; internal set; }

    public ChannelReader<NatsMsg<T>> Msgs => _msgs.Reader;

    public override async ValueTask DisposeAsync()
    {
        if (_msgs.Writer.TryComplete())
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal override ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte> buffer)
    {
        var serializer = Serializer ?? Connection!.Options.Serializer;
        var data = serializer.Deserialize<T>(buffer);
        return _msgs.Writer.WriteAsync(new NatsMsg<T>(data!)
        {
            Connection = Connection,
            Subject = subject,
            ReplyTo = replyTo,
        });
    }
}
