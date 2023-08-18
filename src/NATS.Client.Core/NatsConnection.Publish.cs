using System.Buffers;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        if (opts?.WaitUntilSent ?? false)
        {
            return PubAsync(subject, opts?.ReplyTo, payload, opts?.Headers, cancellationToken);
        }
        else
        {
            return PubPostAsync(subject, opts?.ReplyTo, payload, opts?.Headers, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(in NatsMsg msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PublishAsync(msg.Subject, msg.Data, opts, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T? data, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var serializer = opts?.Serializer ?? Opts.Serializer;
        if (opts?.WaitUntilSent ?? false)
        {
            return PubModelAsync<T>(subject, data, serializer, opts?.ReplyTo, opts?.Headers, cancellationToken);
        }
        else
        {
            return PubModelPostAsync<T>(subject, data, serializer, opts?.ReplyTo, opts?.Headers, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(msg.Subject, msg.Data, opts, cancellationToken);
    }
}
