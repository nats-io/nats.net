using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NATS.Client.Core;

public record NatsMsg(string Subject, ReadOnlyMemory<byte> Data) : NatsMsgBase(Subject);

public record NatsMsg<T>(string Subject, T Data) : NatsMsgBase(Subject);

public abstract record NatsMsgBase(string Subject)
{
    internal INatsCommand? Connection { get; init; }

    public string? ReplyTo { get; init; }

    public NatsHeaders? Headers { get; init; }

    public ValueTask ReplyAsync(ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo!, data, opts, cancellationToken);
    }

    public ValueTask ReplyAsync(NatsMsg msg, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(msg with { Subject = ReplyTo! }, cancellationToken);
    }

    public ValueTask ReplyAsync<TReply>(TReply data, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(ReplyTo!, data, opts, cancellationToken);
    }

    public ValueTask ReplyAsync<TReply>(NatsMsg<TReply> msg)
    {
        CheckReplyPreconditions();
        return Connection.PublishAsync(msg with { Subject = ReplyTo! });
    }

    [MemberNotNull(nameof(Connection))]
    private void CheckReplyPreconditions()
    {
        if (Connection == default)
        {
            throw new NatsException("unable to send reply; message did not originate from a subscription");
        }

        if (string.IsNullOrWhiteSpace(ReplyTo))
        {
            throw new NatsException("unable to send reply; ReplyTo is empty");
        }
    }
}
