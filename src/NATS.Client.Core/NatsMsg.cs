using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NATS.Client.Core;

public readonly record struct NatsMsg(
    string Subject,
    string? ReplyTo,
    NatsHeaders? Headers,
    ReadOnlyMemory<byte> Data,
    INatsConnection? Connection)
{
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

public readonly record struct NatsMsg<T>(
    string Subject,
    string? ReplyTo,
    NatsHeaders? Headers,
    T? Data,
    INatsConnection? Connection)
{
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
