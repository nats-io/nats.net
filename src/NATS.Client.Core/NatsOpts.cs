using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if NETSTANDARD
using Random = NATS.Client.Core.Internal.NetStandardExtensions.Random;
#endif

namespace NATS.Client.Core;

/// <summary>
/// Immutable options for NatsConnection, you can configure via `with` operator.
/// </summary>
public sealed record NatsOpts
{
    public static readonly NatsOpts Default = new();

    /// <summary>
    /// NATS server URL to connect to. (default: nats://localhost:4222)
    /// </summary>
    /// <remarks>
    /// <para>
    /// You can set more than one server as seed servers in a comma-separated list.
    /// The client will randomly select a server from the list to connect to unless
    /// <see cref="NoRandomize"/> (which is <c>false</c> by default) is set to <c>true</c>.
    /// </para>
    /// <para>
    /// User-password or token authentication can be set in the URL.
    /// For example, <c>nats://derek:s3cr3t@localhost:4222</c> or <c>nats://token@localhost:4222</c>.
    /// You can also set the username and password or token separately using <see cref="AuthOpts"/>;
    /// however, if both are set, the <see cref="AuthOpts"/> will take precedence.
    /// You should URL-encode the username and password or token if they contain special characters.
    /// </para>
    /// <para>
    /// If multiple servers are specified and user-password or token authentication is used in the URL,
    /// only the credentials in the first server URL will be used; credentials in the remaining server
    /// URLs will be ignored.
    /// </para>
    /// </remarks>
    public string Url { get; init; } = "nats://localhost:4222";

    public string Name { get; init; } = "NATS .NET Client";

    public bool Echo { get; init; } = true;

    public bool Verbose { get; init; } = false;

    public bool Headers { get; init; } = true;

    public NatsAuthOpts AuthOpts { get; init; } = NatsAuthOpts.Default;

    public NatsTlsOpts TlsOpts { get; init; } = NatsTlsOpts.Default;

    public NatsWebSocketOpts WebSocketOpts { get; init; } = NatsWebSocketOpts.Default;

    public INatsSerializerRegistry SerializerRegistry { get; init; } = NatsDefaultSerializerRegistry.Default;

    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    public int WriterBufferSize { get; init; } = 65536;

    public int ReaderBufferSize { get; init; } = 65536;

    public bool UseThreadPoolCallback { get; init; } = false;

    public string InboxPrefix { get; init; } = "_INBOX";

    public bool NoRandomize { get; init; } = false;

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMinutes(2);

    public int MaxPingOut { get; init; } = 2;

    /// <summary>
    /// Minimum amount of time to wait between reconnect attempts. (default: 2s)
    /// </summary>
    public TimeSpan ReconnectWaitMin { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Random amount of time to wait between reconnect attempts. (default: 100ms)
    /// </summary>
    public TimeSpan ReconnectJitter { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public int ObjectPoolSize { get; init; } = 256;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SubscriptionCleanUpInterval { get; init; } = TimeSpan.FromMinutes(5);

    public Encoding HeaderEncoding { get; init; } = Encoding.ASCII;

    public Encoding SubjectEncoding { get; init; } = Encoding.ASCII;

    public bool WaitUntilSent { get; init; } = false;

    /// <summary>
    /// Maximum number of reconnect attempts. (default: -1, unlimited)
    /// </summary>
    /// <remarks>
    /// Set to -1 for unlimited retries.
    /// </remarks>
    public int MaxReconnectRetry { get; init; } = -1;

    /// <summary>
    /// Backoff delay limit for reconnect attempts. (default: 5 seconds)
    /// </summary>
    /// <remarks>
    /// When the connection is lost, the client will wait for <see cref="ReconnectWaitMin"/> before attempting to reconnect.
    /// Every failed attempt will increase the wait time by 2x, up to <see cref="ReconnectWaitMax"/>.
    /// If <see cref="ReconnectWaitMax"/> is equal to or less than <see cref="ReconnectWaitMin"/>, the delay will be constant.
    /// </remarks>
    public TimeSpan ReconnectWaitMax { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Opts-out of the default connect behavior of aborting
    /// subsequent reconnect attempts if server returns the same auth error twice.
    /// </summary>
    public bool IgnoreAuthErrorAbort { get; init; } = false;

    /// <summary>
    /// This value will be used for subscriptions internal bounded message channel capacity.
    /// The default subscriber pending message limit is 1024.
    /// </summary>
    public int SubPendingChannelCapacity { get; init; } = 1024;

    /// <summary>
    /// This value will be used for subscriptions internal bounded message channel <c>FullMode</c>.
    /// The default is to drop newest message when full (<c>BoundedChannelFullMode.DropNewest</c>).
    /// </summary>
    /// <remarks>
    /// If the client reaches this internal limit (bounded channel capacity), by default it will drop messages
    /// and continue to process new messages. This is aligned with NATS at most once delivery. It is up to
    /// the application to detect the missing messages (<seealso cref="NatsConnection.MessageDropped"/>) and recover
    /// from this condition or set a different default such as <c>BoundedChannelFullMode.Wait</c> in which
    /// case it might risk server disconnecting the client as a slow consumer.
    /// </remarks>
    public BoundedChannelFullMode SubPendingChannelFullMode { get; init; } = BoundedChannelFullMode.DropNewest;

    public Func<NatsUri, NatsOpts, CancellationToken, ValueTask<ISocketConnection>>? SocketConnectionFactory { get; init; } = null;

    internal NatsUri[] GetSeedUris(bool suppressRandomization = false)
    {
        var urls = Url.Split(',');
        return NoRandomize || suppressRandomization
            ? urls.Select(x => new NatsUri(x, true)).Distinct().ToArray()
            : urls.Select(x => new NatsUri(x, true)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray();
    }

    internal NatsOpts ReadUserInfoFromConnectionString()
    {
        // Setting credentials in options takes precedence over URL credentials
        if (AuthOpts.Username is { Length: > 0 } || AuthOpts.Password is { Length: > 0 } || AuthOpts.Token is { Length: > 0 })
        {
            return this;
        }

        var natsUri = GetSeedUris(suppressRandomization: true).First();
        var uriBuilder = new UriBuilder(natsUri.Uri);

        if (uriBuilder.UserName is not { Length: > 0 })
        {
            return this;
        }

        if (uriBuilder.Password is { Length: > 0 })
        {
            return this with
            {
                AuthOpts = AuthOpts with
                {
                    Username = Uri.UnescapeDataString(uriBuilder.UserName),
                    Password = Uri.UnescapeDataString(uriBuilder.Password),
                },
            };
        }
        else
        {
            return this with
            {
                AuthOpts = AuthOpts with
                {
                    Token = Uri.UnescapeDataString(uriBuilder.UserName),
                },
            };
        }
    }
}

public static class NatsOptsExtensions
{
    /// <summary>
    /// Applies an exponential backoff delay with an added random jitter for attempts.
    /// </summary>
    /// <param name="opts">The NatsOpts instance containing configuration settings for intervals and jitter.</param>
    /// <param name="iter">The current attempt iteration, used to calculate the exponential delay.</param>
    /// <returns>A task that completes after the calculated delay time has elapsed.</returns>
    public static Task BackoffWithJitterAsync(this NatsOpts opts, int iter)
    {
        var baseDelay = opts.ReconnectWaitMin.TotalMilliseconds * Math.Pow(2, iter - 1);
        var jitter = opts.ReconnectJitter.TotalMilliseconds * Random.Shared.NextDouble();

        var delay = Math.Min(baseDelay + jitter, opts.ReconnectWaitMax.TotalMilliseconds);
        return Task.Delay(TimeSpan.FromMilliseconds(delay));
    }
}
