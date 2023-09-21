using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore;

public class NatsKVContext
{
    private readonly NatsJSContext _context;
    private readonly NatsKVOpts _opts;

    public NatsKVContext(NatsJSContext context, NatsKVOpts? opts = default)
    {
        _context = context;
        _opts = opts ?? new NatsKVOpts();
    }

    public async ValueTask<NatsKVStore> CreateStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var stream = await _context.CreateStreamAsync(
            new StreamConfiguration
            {
                Name = $"KV_{bucket}",
                Subjects = new[] { $"$KV.{bucket}.>" },
                MaxMsgsPerSubject = 5,
                Retention = StreamConfigurationRetention.limits,
                Discard = StreamConfigurationDiscard.@new,
                DuplicateWindow = 120000000000,
                AllowRollupHdrs = true,
                DenyDelete = true,
                AllowDirect = true,
            },
            cancellationToken);
        return new NatsKVStore(bucket, _context, stream);
    }
}

public class NatsKVStore
{
    private readonly string _bucket;
    private readonly NatsJSContext _context;
    private readonly NatsJSStream _stream;

    internal NatsKVStore(string bucket, NatsJSContext context, NatsJSStream stream)
    {
        _bucket = bucket;
        _context = context;
        _stream = stream;
    }

    public async ValueTask PutAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        // PUB $KV.profiles.sue.color
        var ack = await _context.PublishAsync($"$KV.{_bucket}.{key}", value, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        // API--------------+ stream----+ subject--------------+
        // $JS.API.DIRECT.GET.KV_profiles.$KV.profiles.sue.color
        return _stream.GetAsync<T>($"$KV.{_bucket}.{key}", cancellationToken);
    }
}

public record NatsKVOpts
{
}

public enum NatsKVStorageType
{
    File = 0,
    Memory = 1,
}

public record NatsKVRepublish
{
    /// <summary>
    /// Subject that should be republished.
    /// </summary>
    public string src { get; init; }

    /// <summary>
    /// Subject where messages will be republished.
    /// </summary>
    public string dest { get; init; }

    /// <summary>
    /// If true, only headers should be republished.
    /// </summary>
    public bool headers_only { get; init; }
}

public record NatsKVConfig
{
    /// <summary>
    /// Name of the bucket
    /// </summary>
    public string Bucket { get; init; }

    /// <summary>
    /// Human readable description.
    /// </summary>
    public string Description { get; init; }

    /// <summary>
    /// Maximum size of a single value.
    /// </summary>
    public int MaxValueSize { get; init; }

    /// <summary>
    /// Maximum historical entries.
    /// </summary>
    public long History { get; init; }

    /// <summary>
    /// Maximum age of any entry in the bucket, expressed in nanoseconds
    /// </summary>
    public TimeSpan MaxAge { get; init; }

    /// <summary>
    /// How large the bucket may become in total bytes before the configured discard policy kicks in
    /// </summary>
    public long MaxBytes { get; init; }

    /// <summary>
    /// The type of storage backend, `File` (default) and `Memory`
    /// </summary>
    public NatsKVStorageType Storage { get; init; }

    /// <summary>
    /// How many replicas to keep for each entry in a cluster.
    /// </summary>
    public int NumberOfReplicas { get; init; }

    /// <summary>
    /// Republish is for republishing messages once persistent in the Key Value Bucket.
    /// </summary>
    public NatsKVRepublish? Republish { get; init; }

    // Bucket mirror configuration.
    // pub mirror: Option<Source>,
    // Bucket sources configuration.
    // pub sources: Option<Vec<Source>>,
    // Allow mirrors using direct API.
    // pub mirror_direct: bool,
}
