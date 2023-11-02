namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        if (opts?.WaitUntilSent ?? false)
        {
            return PubAsync(subject, replyTo, payload: default, headers, cancellationToken);
        }
        else
        {
            return PubPostAsync(subject, replyTo, payload: default, headers, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsHeaders? headers = default, string? replyTo = default, INatsSerializer<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        serializer ??= Opts.Serializers.GetSerializer<T>();
        if (opts?.WaitUntilSent ?? false)
        {
            return PubModelAsync<T>(subject, data, serializer, replyTo, headers, cancellationToken);
        }
        else
        {
            return PubModelPostAsync<T>(subject, data, serializer, replyTo, headers, opts?.ErrorHandler, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerializer<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(msg.Subject, msg.Data, msg.Headers, msg.ReplyTo, serializer, opts, cancellationToken);
    }
}
