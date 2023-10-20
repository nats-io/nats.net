using NATS.Client.Core;

namespace NATS.Client.Services;

public struct NatsSvcMsg
{
    private readonly NatsMsg<NatsMemoryOwner<byte>> _msg;

    public NatsSvcMsg(NatsMsg<NatsMemoryOwner<byte>> msg) => _msg = msg;

    public string Subject => _msg.Subject;

    public NatsMemoryOwner<byte> Data => _msg.Data;

    public string? ReplyTo => _msg.ReplyTo;

    public ValueTask ReplyAsync(NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(headers, replyTo, opts, cancellationToken);

    public ValueTask ReplyAsync<T>(T data, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(data, headers, replyTo, opts, cancellationToken);
}
