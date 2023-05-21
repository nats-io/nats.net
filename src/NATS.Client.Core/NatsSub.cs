using System.Threading.Channels;

namespace NATS.Client.Core;

public sealed class NatsSub : NatsSubBase
{
    private readonly Channel<NatsMsg> _msgs = Channel.CreateUnbounded<NatsMsg>();

    public ChannelReader<NatsMsg> Msgs => _msgs.Reader;

    public NatsSub Register(Action<NatsMsg> action)
    {
        // XXX
#pragma warning disable VSTHRD110
        Task.Run(async () =>
#pragma warning restore VSTHRD110
        {
            await foreach (var natsMsg in _msgs.Reader.ReadAllAsync())
            {
                action(natsMsg);
            }
        });

        return this;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_msgs.Writer.TryComplete())
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal override bool Receive(NatsMsg msg)
    {
        return _msgs.Writer.TryWrite(msg);
    }
}

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

    internal abstract bool Receive(NatsMsg msg);
}

public sealed class NatsSub<T> : NatsSubBase
{
    private readonly Channel<NatsMsg<T>> _msgs = Channel.CreateUnbounded<NatsMsg<T>>();

    public INatsSerializer? Serializer { get; internal set; }

    public ChannelReader<NatsMsg<T>> Msgs => _msgs.Reader;

    public NatsSub<T> Register(Action<NatsMsg<T>> action)
    {
        // XXX
#pragma warning disable VSTHRD110
        Task.Run(async () =>
#pragma warning restore VSTHRD110
        {
            await foreach (var natsMsg in _msgs.Reader.ReadAllAsync())
            {
                action(natsMsg);
            }
        });

        return this;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_msgs.Writer.TryComplete())
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal override bool Receive(NatsMsg msg)
    {
        var serializer = Serializer ?? Connection!.Options.Serializer;
        var tData = serializer.Deserialize<T>(msg.Data);
        return _msgs.Writer.TryWrite(new NatsMsg<T>(tData!)
        {
            Connection = msg.Connection,
            SubjectKey = msg.SubjectKey,
            ReplyToKey = msg.ReplyToKey,
            Headers = msg.Headers,
        });
    }
}
