using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core.Internal;
#if NETSTANDARD
using Random = NATS.Client.Core.Internal.NetStandardExtensions.Random;
#endif

namespace NATS.Client.Core;

/// <summary>
/// Specifies the modes for handling request-reply interactions in NATS.
/// </summary>
public enum NatsRequestReplyMode
{
    /// <summary>
    /// Uses a shared inbox for all requests.
    /// </summary>
    SharedInbox,

    /// <summary>
    /// Uses a direct reply for each request.
    /// </summary>
    Direct,
}

/// <summary>
/// Immutable options for NatsConnection, you can configure via `with` operator.
/// </summary>
/// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.NatsContext">Load NatsOpts from NATS CLI context files</seealso>
public sealed record NatsOpts
{
    public static readonly NatsOpts Default = new()
    {
        WebSocketOpts = NatsWebSocketOpts.Default,
        TlsOpts = NatsTlsOpts.Default,
        AuthOpts = NatsAuthOpts.Default,
    };

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

    public NatsAuthOpts AuthOpts { get; init; } = new();

    public NatsTlsOpts TlsOpts { get; init; } = new();

    public NatsWebSocketOpts WebSocketOpts { get; init; } = new();

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

    /// <summary>
    /// Gets or sets encoding used for NATS message header names and values. (default: ASCII)
    /// </summary>
    /// <remarks>
    /// NATS headers follow HTTP/1.1 conventions where header field values are
    /// restricted to visible US-ASCII characters per RFC 9110. Use base64 encoding
    /// for non-ASCII data in header values.
    /// </remarks>
    public Encoding HeaderEncoding { get; init; } = Encoding.ASCII;

    /// <summary>
    /// Gets or sets encoding used for NATS subjects and reply-to addresses. (default: UTF-8)
    /// </summary>
    /// <remarks>
    /// The NATS protocol specifies that subjects are UTF-8 encoded on the wire.
    /// UTF-8 is backwards compatible with ASCII, so existing ASCII-only subjects
    /// are unaffected by this default.
    /// </remarks>
    public Encoding SubjectEncoding { get; init; } = Encoding.UTF8;

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

    /// <summary>
    /// Determines the mechanism for handling request-reply interactions in NATS.
    /// </summary>
    /// <remarks>
    /// There are two available modes:
    /// <para>
    /// 1. <see cref="NatsRequestReplyMode.SharedInbox"/> - Uses a shared subscription inbox for handling replies
    /// to request messages, using the muxer.
    /// </para>
    /// <para>
    /// 2. <see cref="NatsRequestReplyMode.Direct"/> - While using the same inbox prefix, each reply
    /// is handled before being processed by the muxer. This mode is more resource-efficient.
    /// </para>
    /// The <see cref="RequestReplyMode"/> setting determines which mode is used during message exchanges
    /// initiated by <see cref="NatsConnection.RequestAsync{TRequest, TReply}"/> or other related methods.
    /// </remarks>
    public NatsRequestReplyMode RequestReplyMode { get; init; } = NatsRequestReplyMode.SharedInbox;

    /// <summary>
    /// Factory for creating socket connections to the NATS server.
    /// When set, this factory will be used instead of the default connection implementation.
    /// For the library to handle TLS upgrade automatically, implement the <see cref="INatsTlsUpgradeableSocketConnection"/> interface.
    /// </summary>
    /// <seealso cref="INatsTlsUpgradeableSocketConnection"/>
    public INatsSocketConnectionFactory? SocketConnectionFactory { get; init; }

    /// <summary>
    /// Determines whether the client should retry connecting to the server during the initial connection
    /// attempt if the connection fails. (default: <c>false</c>)
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property controls the behavior of the initial connection process.
    /// </para>
    /// <para>
    /// When set to <c>true</c>, the client will periodically retry connecting to the server based on the
    /// configured retry intervals until a successful connection is established or the operation is explicitly canceled.
    /// </para>
    /// <para>
    /// When set to <c>false</c> (default behavior), the client will not retry and will throw an exception
    /// if the connection cannot be established initially.
    /// </para>
    /// <para>
    /// The retry intervals are influenced by options such as <see cref="ReconnectWaitMin"/> and <see cref="ReconnectWaitMax"/>, with the potential addition of <see cref="ReconnectJitter"/> for randomized delays between retries.
    /// This property is particularly useful in scenarios where transient network failures or server unavailability are expected during the first connection attempt.
    /// </para>
    /// </remarks>
    public bool RetryOnInitialConnect { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether publish would throw an exception
    /// when the connection is disconnected and the <see cref="CommandTimeout"/> is reached.
    /// The default is <c>false</c>, meaning publish will not throw on disconnected state
    /// and will wait to publish the message until reconnected.
    /// </summary>
    public bool PublishTimeoutOnDisconnected { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to skip subject validation.
    /// The default is <c>true</c>, meaning subject validation is disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set to <c>true</c> (default), all subject validation is bypassed.
    /// </para>
    /// <para>
    /// When set to <c>false</c>, subjects are validated to ensure they are not empty
    /// and don't contain whitespace characters (space, tab, CR, LF). This can help
    /// catch invalid subjects early but adds minor overhead.
    /// </para>
    /// </remarks>
    public bool SkipSubjectValidation { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to suppress warning logs when a slow consumer is detected.
    /// The default is <c>false</c>, meaning warnings will be logged once per slow consumer episode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a subscription becomes a slow consumer (dropping messages due to channel capacity limits),
    /// a warning is logged once. The warning will be logged again if the subscription recovers
    /// (channel drains to nearly empty) and then becomes slow again.
    /// </para>
    /// <para>
    /// Note that the <see cref="NatsConnection.MessageDropped"/> and <see cref="NatsConnection.SlowConsumerDetected"/>
    /// events will still fire regardless of this setting.
    /// </para>
    /// </remarks>
    public bool SuppressSlowConsumerWarnings { get; init; } = false;

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
    /// <param name="cancellationToken">Cancel back-off delay.</param>
    /// <returns>A task that completes after the calculated delay time has elapsed.</returns>
    public static Task BackoffWithJitterAsync(this NatsOpts opts, int iter, CancellationToken cancellationToken = default)
    {
        if (iter < 1)
        {
            // Ensure iter is at least 1 to avoid negative or zero delay calculations
            iter = 1;
        }

        if (iter > 31)
        {
            // This is to prevent infinity in Math.Pow()
            iter = 31;
        }

        var baseDelay = opts.ReconnectWaitMin.TotalMilliseconds * Math.Pow(2, iter - 1);
        var jitter = opts.ReconnectJitter.TotalMilliseconds * Random.Shared.NextDouble();

        var delay = Math.Min(baseDelay + jitter, opts.ReconnectWaitMax.TotalMilliseconds);
        return Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
    }
}
