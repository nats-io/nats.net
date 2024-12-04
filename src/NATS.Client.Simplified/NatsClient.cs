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
    /// <param name="url">NATS server URL to connect to. (default: nats://localhost:4222)</param>
    /// <param name="name">Client name. (default: NATS .NET Client)</param>
    /// <param name="credsFile">Credentials filepath.</param>
    /// <remarks>
    /// <para>
    /// You can set more than one server as seed servers in a comma-separated list in the <paramref name="url"/>.
    /// The client will randomly select a server from the list to connect.
    /// </para>
    /// <para>
    /// User-password or token authentication can be set in the <paramref name="url"/>.
    /// For example, <c>nats://derek:s3cr3t@localhost:4222</c> or <c>nats://token@localhost:4222</c>.
    /// You should URL-encode the username and password or token if they contain special characters.
    /// </para>
    /// <para>
    /// If multiple servers are specified and user-password or token authentication is used in the <paramref name="url"/>,
    /// only the credentials in the first server URL will be used; credentials in the remaining server
    /// URLs will be ignored.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsClient"/> class.
    /// </summary>
    /// <param name="opts">NATS client options.</param>
    /// <param name="pending">Sets `SubPendingChannelFullMode` option. (default: wait)</param>
    /// <remarks>
    /// By default, the <paramref name="opts"/> will be merged with the default options
    /// overriding SerializationRegistry with <see cref="NatsClientDefaultSerializerRegistry.Default"/>
    /// and SubPendingChannelFullMode with <see cref="BoundedChannelFullMode.Wait"/>.
    /// </remarks>
    public NatsClient(NatsOpts opts, BoundedChannelFullMode pending = BoundedChannelFullMode.Wait)
    {
        if (ReferenceEquals(opts.SerializerRegistry, NatsOpts.Default.SerializerRegistry))
        {
            opts = opts with
            {
                SerializerRegistry = NatsClientDefaultSerializerRegistry.Default,
            };
        }

        opts = opts with { SubPendingChannelFullMode = pending };

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
    public ValueTask ReconnectAsync() => Connection.ReconnectAsync();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
