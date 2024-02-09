using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

/// <summary>
/// Immutable options for NatsConnection, you can configure via `with` operator.
/// </summary>
public sealed record NatsOpts
{
    public static readonly NatsOpts Default = new();

    public string Url { get; init; } = "nats://localhost:4222";

    public string Name { get; init; } = "NATS .Net Client";

    public bool Echo { get; init; } = true;

    public bool Verbose { get; init; } = false;

    public bool Headers { get; init; } = true;

    public NatsAuthOpts AuthOpts { get; init; } = NatsAuthOpts.Default;

    public NatsTlsOpts TlsOpts { get; init; } = NatsTlsOpts.Default;

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

    internal NatsUri[] GetSeedUris()
    {
        var urls = Url.Split(',');
        return NoRandomize
            ? urls.Select(x => new NatsUri(x, true)).Distinct().ToArray()
            : urls.Select(x => new NatsUri(x, true)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray();
    }
}
