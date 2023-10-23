using NATS.Client.Core;

namespace NATS.Client.Services;

public readonly struct NatsSvcMsg<T>
{
    private readonly NatsMsg<T> _msg;
    private readonly NatsSvcEndPointBase? _endPoint;

    public NatsSvcMsg(NatsMsg<T> msg, NatsSvcEndPointBase? endPoint, Exception? exception)
    {
        Exception = exception;
        _msg = msg;
        _endPoint = endPoint;
    }

    public bool HasError => Exception is not null;

    public Exception? Exception { get; }

    public string Subject => _msg.Subject;

    public T? Data => _msg.Data;

    public string? ReplyTo => _msg.ReplyTo;

    public ValueTask ReplyAsync(NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(headers, replyTo, opts, cancellationToken);

    public ValueTask ReplyAsync<TReply>(TReply data, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(data, headers, replyTo, opts, cancellationToken);

    public ValueTask ReplyErrorAsync<TReply>(int code, string message, TReply data, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        headers ??= new NatsHeaders();
        headers.Add("Nats-Service-Error-Code", $"{code}");
        headers.Add("Nats-Service-Error", $"{message}");

        _endPoint?.IncrementErrors();
        _endPoint?.SetLastError($"{message} ({code})");

        return ReplyAsync(data, headers, replyTo, opts, cancellationToken);
    }

    public ValueTask ReplyErrorAsync(int code, string message, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        headers ??= new NatsHeaders();
        headers.Add("Nats-Service-Error-Code", $"{code}");
        headers.Add("Nats-Service-Error", $"{message}");

        _endPoint?.IncrementErrors();
        _endPoint?.SetLastError($"{message} ({code})");

        return ReplyAsync(headers: headers, replyTo: replyTo, opts: opts, cancellationToken: cancellationToken);
    }
}
