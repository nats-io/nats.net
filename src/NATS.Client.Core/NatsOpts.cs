using System.Text;
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

    public int WriterBufferSize { get; init; } = 65534;

    public int ReaderBufferSize { get; init; } = 1048576;

    public bool UseThreadPoolCallback { get; init; } = false;

    public string InboxPrefix { get; init; } = "_INBOX";

    public bool NoRandomize { get; init; } = false;

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMinutes(2);

    public int MaxPingOut { get; init; } = 2;

    /// <summary>
    /// Minimum amount of time to wait between reconnect attempts. (default: 2s)
    /// </summary>
    public TimeSpan ReconnectWait { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Random amount of time to wait between reconnect attempts. (default: 100ms)
    /// </summary>
    public TimeSpan ReconnectJitter { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public int ObjectPoolSize { get; init; } = 256;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan SubscriptionCleanUpInterval { get; init; } = TimeSpan.FromMinutes(5);

    public int? WriterCommandBufferLimit { get; init; } = 1_000;

    public Encoding HeaderEncoding { get; init; } = Encoding.ASCII;

    public bool WaitUntilSent { get; init; } = false;

    /// <summary>
    /// Maximum number of reconnect attempts. (default: -1, unlimited)
    /// </summary>
    /// <remarks>
    /// Set to -1 for unlimited retries.
    /// </remarks>
    public int MaxReconnectRetry { get; init; } = -1;

    /// <summary>
    /// Backoff policy for reconnect attempts. (default: 0, 250, 250, 500, 500, 3000, 5000)
    /// </summary>
    /// <remarks>
    /// This is a list of milliseconds to wait between reconnect attempts where the next element is used with every attempt.
    /// When the number of retry attempts reaches the end of the list, it will keep using the last element
    /// which is 5 seconds by default.
    /// </remarks>
    public int[] ReconnectBackoffPolicyMilliseconds { get; init; } = { 0, 250, 250, 500, 500, 3000, 5000 };

    internal NatsUri[] GetSeedUris()
    {
        var urls = Url.Split(',');
        return NoRandomize
            ? urls.Select(x => new NatsUri(x, true)).Distinct().ToArray()
            : urls.Select(x => new NatsUri(x, true)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray();
    }
}
