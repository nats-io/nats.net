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
    internal static NatsMsg Build(
        string subject,
        string? replyTo,
        in ReadOnlySequence<byte>? headersBuffer,
        in ReadOnlySequence<byte> payloadBuffer,
        INatsConnection? connection,
        HeaderParser headerParser)
    {
        NatsHeaders? headers = null;

        if (headersBuffer != null)
        {
            headers = new NatsHeaders();
            if (!headerParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
            {
                throw new NatsException("Error parsing headers");
            }

            headers.SetReadOnly();
        }

        return new NatsMsg(subject, replyTo, headers, payloadBuffer.ToArray(), connection);
    }

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
    internal static NatsMsg<T> Build(
        string subject,
        string? replyTo,
        in ReadOnlySequence<byte>? headersBuffer,
        in ReadOnlySequence<byte> payloadBuffer,
        INatsConnection? connection,
        HeaderParser headerParser,
        INatsSerializer serializer)
    {
        var data = serializer.Deserialize<T>(payloadBuffer);

        NatsHeaders? headers = null;

        if (headersBuffer != null)
        {
            headers = new NatsHeaders();
            if (!headerParser.ParseHeaders(new SequenceReader<byte>(headersBuffer.Value), headers))
            {
                throw new NatsException("Error parsing headers");
            }

            headers.SetReadOnly();
        }

        return new NatsMsg<T>(subject, replyTo, headers, data, connection);
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
