using System.Threading.Channels;
using NATS.Client.Core;

namespace NATS.Net;

/// <summary>
/// Represents a NATS client that provides methods for interacting with NATS server.
/// </summary>
public class NatsClient : INatsClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NatsClient"/> class.
    /// </summary>
    /// <param name="url">NATS server URL</param>
    /// <param name="name">Client name</param>
    /// <param name="credsFile">Credentials filepath</param>
    public NatsClient(
        string url = "nats://localhost:4222",
        string name = "NATS .NET Client",
        string? credsFile = default)
    {
        var opts = new NatsOpts
        {
            Name = name,
            Url = url,
            SerializerRegistry = NatsClientDefaultSerializerRegistry.Default,
            SubPendingChannelFullMode = BoundedChannelFullMode.Wait,
            AuthOpts = new NatsAuthOpts { CredsFile = credsFile },
        };

        Connection = new NatsConnection(opts);
    }

    /// <inheritdoc />
    public INatsConnection Connection { get; }

    /// <inheritdoc />
    public ValueTask ConnectAsync() => Connection.ConnectAsync();

    /// <inheritdoc />
    public ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default) => Connection.PingAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(string subject, T data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
        => Connection.PublishAsync(subject, data, headers, replyTo, serializer, opts, cancellationToken);

    /// <inheritdoc />
    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
        => Connection.PublishAsync(subject, headers, replyTo, opts, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default)
        => Connection.SubscribeAsync(subject, queueGroup, serializer, opts, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(string subject, TRequest? data, NatsHeaders? headers = default, INatsSerialize<TRequest>? requestSerializer = default, INatsDeserialize<TReply>? replySerializer = default, NatsPubOpts? requestOpts = default, NatsSubOpts? replyOpts = default, CancellationToken cancellationToken = default)
        => Connection.RequestAsync(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NatsMsg<TReply>> RequestAsync<TReply>(string subject, INatsDeserialize<TReply>? replySerializer = default, NatsSubOpts? replyOpts = default, CancellationToken cancellationToken = default)
        => Connection.RequestAsync(subject, replySerializer, replyOpts, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
