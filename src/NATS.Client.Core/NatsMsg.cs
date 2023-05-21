using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NATS.Client.Core;

public record NatsMsg : NatsMsgBase
{
    public ReadOnlySequence<byte> Data { get; set; }
}

public abstract record NatsMsgBase
{
    internal INatsCommand? Connection { get; init; }

    internal NatsKey SubjectKey { get; set; }

    public string Subject
    {
        get => SubjectKey.Key;
        set => SubjectKey = new NatsKey(value);
    }

    internal NatsKey ReplyToKey { get; set; }

    public string ReplyTo
    {
        get => ReplyToKey.Key;
        set => ReplyToKey = new NatsKey(value);
    }

    public NatsHeaders? Headers { get; set; }

    public ValueTask ReplyAsync(ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo, data, opts, cancellationToken);
    }

    public ValueTask ReplyAsync(NatsMsg msg, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        msg.SubjectKey = ReplyToKey;
        return Connection.PublishAsync(msg, cancellationToken);
    }

    public ValueTask ReplyAsync<TReply>(TReply data, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo, data, opts, cancellationToken);
    }

    public ValueTask ReplyAsync<TReply>(NatsMsg<TReply> msg)
    {
        CheckReplyPreconditions();
        msg.SubjectKey = ReplyToKey;
        return Connection.PublishAsync(msg);
    }

    public void PostReply(ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default)
    {
        CheckReplyPreconditions();
        Connection.PostPublish(ReplyTo, data, opts);
    }

    public void PostReply(NatsMsg msg)
    {
        CheckReplyPreconditions();
        msg.SubjectKey = ReplyToKey;
        Connection.PostPublish(msg);
    }

    public void PostReply<TReply>(TReply data, in NatsPubOpts? opts = default)
    {
        CheckReplyPreconditions();
        Connection.PostPublish(ReplyTo, data, opts);
    }

    public void PostReply<TReply>(NatsMsg<TReply> msg)
    {
        CheckReplyPreconditions();
        msg.SubjectKey = ReplyToKey;
        Connection.PostPublish(msg);
    }

    [MemberNotNull(nameof(Connection))]
    private void CheckReplyPreconditions()
    {
        if (Connection == default)
        {
            throw new NatsException("unable to send reply; message did not originate from a subscription");
        }

        if (string.IsNullOrEmpty(ReplyToKey.Key) && ReplyToKey.Buffer?.Length == 0)
        {
            throw new NatsException("unable to send reply; ReplyTo is empty");
        }
    }

    private void CheckPublishPreconditions()
    {
        if (string.IsNullOrEmpty(SubjectKey.Key) && SubjectKey.Buffer?.Length == 0)
        {
            throw new NatsException("unable to publish; Subject is empty");
        }
    }
}

public record NatsMsg<T>(T Data) : NatsMsgBase
{
    public T Data { get; set; } = Data;

    public INatsSerializer? Serializer { get; set; }
}
