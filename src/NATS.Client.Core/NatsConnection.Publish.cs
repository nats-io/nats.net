using System.Buffers;

namespace NATS.Client.Core;

public partial class NatsConnection
{
    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, ReadOnlySequence<byte> payload = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        return PubAsync(subject, opts?.ReplyTo, payload, opts?.Headers, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(NatsMsg msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync(msg.Subject, msg.Data, default, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T data, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var serializer = opts?.Serializer ?? Options.Serializer;
        return PubModelAsync<T>(subject, data, serializer, opts?.ReplyTo, opts?.Headers, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(NatsMsg<T> msg, CancellationToken cancellationToken = default)
    {
        return PublishAsync<T>(msg.Subject, msg.Data, default, cancellationToken);
    }
}
