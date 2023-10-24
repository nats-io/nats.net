using System.Text.RegularExpressions;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Key Value Store storage type
/// </summary>
public enum NatsKVStorageType
{
    File = 0,
    Memory = 1,
}

/// <summary>
/// Key Value Store context
/// </summary>
public class NatsKVContext
{
    private static readonly Regex ValidBucketRegex = new(pattern: @"\A[a-zA-Z0-9_-]+\z", RegexOptions.Compiled);
    private static readonly Regex ValidKeyRegex = new(pattern: @"\A[-/_=\.a-zA-Z0-9]+\z", RegexOptions.Compiled);

    private readonly NatsJSContext _context;
    private readonly NatsKVOpts _opts;

    /// <summary>
    /// Create a new Key Value Store context
    /// </summary>
    /// <param name="context">JetStream context</param>
    /// <param name="opts">Key Value Store context options</param>
    public NatsKVContext(NatsJSContext context, NatsKVOpts? opts = default)
    {
        _context = context;
        _opts = opts ?? new NatsKVOpts();
    }

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    public ValueTask<NatsKVStore> CreateStoreAsync(string bucket, CancellationToken cancellationToken = default)
        => CreateStoreAsync(new NatsKVConfig(bucket), cancellationToken);

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    public async ValueTask<NatsKVStore> CreateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        // TODO: KV Mirrors
        var subjects = new[] { $"$KV.{config.Bucket}.>" };

        long history;
        if (config.History > 0)
        {
            if (config.History > NatsKVDefaults.MaxHistory)
            {
                throw new NatsKVException($"Too long history (max:{NatsKVDefaults.MaxHistory})");
            }
            else
            {
                history = config.History;
            }
        }
        else
        {
            history = 1;
        }

        var storage = config.Storage == NatsKVStorageType.File
            ? StreamConfigurationStorage.file
            : StreamConfigurationStorage.memory;

        var republish = config.Republish != null
            ? new Republish
            {
                Dest = config.Republish.Dest!,
                Src = config.Republish.Src!,
                HeadersOnly = config.Republish.HeadersOnly,
            }
            : null;

        var replicas = config.NumberOfReplicas > 0 ? config.NumberOfReplicas : 1;

        var streamConfig = new StreamConfiguration
        {
            Name = BucketToStream(config.Bucket),
            Description = config.Description!,
            Subjects = subjects,
            MaxMsgsPerSubject = history,
            MaxBytes = config.MaxBytes,
            MaxAge = config.MaxAge.ToNanos(),
            MaxMsgSize = config.MaxValueSize,
            Storage = storage,
            Republish = republish!,
            AllowRollupHdrs = true,
            DenyDelete = true,
            DenyPurge = false,
            AllowDirect = true,
            NumReplicas = replicas,
            Discard = StreamConfigurationDiscard.@new,

            // TODO: KV mirrors
            // MirrorDirect =
            // Mirror =
            Retention = StreamConfigurationRetention.limits, // from ADR-8
            DuplicateWindow = 120000000000, // from ADR-8
        };

        var stream = await _context.CreateStreamAsync(streamConfig, cancellationToken);

        return new NatsKVStore(config.Bucket, _opts, _context, stream);
    }

    /// <summary>
    /// Get a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucjet</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    public async ValueTask<NatsKVStore> GetStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);

        var stream = await _context.GetStreamAsync(BucketToStream(bucket), cancellationToken: cancellationToken);

        if (stream.Info.Config.MaxMsgsPerSubject < 1)
        {
            throw new NatsKVException("Invalid KV store name");
        }

        // TODO: KV mirror
        return new NatsKVStore(bucket, _opts, _context, stream);
    }

    /// <summary>
    /// Delete a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>True for success</returns>
    public ValueTask<bool> DeleteStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);
        return _context.DeleteStreamAsync(BucketToStream(bucket), cancellationToken);
    }

    internal static void ValidateBucketName(string bucket)
    {
        if (!ValidBucketRegex.IsMatch(bucket))
        {
            throw new NatsKVException("Invalid bucket name");
        }
    }

    internal static void ValidateKeyName(string key)
    {
        if (!ValidKeyRegex.IsMatch(key))
        {
            throw new NatsKVException("Invalid key name");
        }
    }

    private static string BucketToStream(string bucket) => $"KV_{bucket}";
}
