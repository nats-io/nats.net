using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NATS.Client.JetStream;
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
public class NatsKVContext : INatsKVContext
{
    private const string KvStreamNamePrefix = "KV_";
    private static readonly int KvStreamNamePrefixLen = KvStreamNamePrefix.Length;
    private static readonly Regex ValidBucketRegex = new(pattern: @"\A[a-zA-Z0-9_-]+\z", RegexOptions.Compiled);

    private readonly NatsJSContext _context;

    /// <summary>
    /// Create a new Key Value Store context
    /// </summary>
    /// <param name="context">JetStream context</param>
    public NatsKVContext(NatsJSContext context) => _context = context;

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<INatsKVStore> CreateStoreAsync(string bucket, CancellationToken cancellationToken = default)
        => CreateStoreAsync(new NatsKVConfig(bucket), cancellationToken);

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask<INatsKVStore> CreateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var streamConfig = NatsKVContext.CreateStreamConfig(config);

        var stream = await _context.CreateStreamAsync(streamConfig, cancellationToken);

        return new NatsKVStore(config.Bucket, _context, stream);
    }

    /// <summary>
    /// Get a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucjet</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask<INatsKVStore> GetStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);

        var stream = await _context.GetStreamAsync(BucketToStream(bucket), cancellationToken: cancellationToken);

        if (stream.Info.Config.MaxMsgsPerSubject < 1)
        {
            throw new NatsKVException("Invalid KV store name");
        }

        // TODO: KV mirror
        return new NatsKVStore(bucket, _context, stream);
    }

    /// <summary>
    /// Update a key value store configuration. Storage type cannot change.
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken"> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask<INatsKVStore> UpdateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var streamConfig = NatsKVContext.CreateStreamConfig(config);

        var stream = await _context.UpdateStreamAsync(streamConfig, cancellationToken);

        return new NatsKVStore(config.Bucket, _context, stream);
    }

    /// <summary>
    /// Delete a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>True for success</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<bool> DeleteStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);
        return _context.DeleteStreamAsync(BucketToStream(bucket), cancellationToken);
    }

    /// <summary>
    /// Get a list of bucket names
    /// </summary>
    /// <param name="cancellationToken"> used to cancel the API call.</param>
    /// <returns>Async enumerable of bucket names. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async IAsyncEnumerable<string> GetBucketNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var name in _context.ListStreamNamesAsync(cancellationToken: cancellationToken))
        {
            if (!name.StartsWith(KvStreamNamePrefix))
            {
                continue;
            }

            yield return ExtractBucketName(name);
        }
    }

    /// <summary>
    /// Gets the status for all buckets
    /// </summary>
    /// <param name="cancellationToken"> used to cancel the API call.</param>
    /// <returns>Async enumerable of Key/Value statuses. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async IAsyncEnumerable<NatsKVStatus> GetStatusesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var name in _context.ListStreamNamesAsync(cancellationToken: cancellationToken))
        {
            var stream = await _context.GetStreamAsync(name, cancellationToken: cancellationToken);
            var isCompressed = stream.Info.Config.Compression != StreamConfigCompression.None;
            yield return new NatsKVStatus(name, isCompressed, stream.Info);
        }
    }

    internal static void ValidateBucketName(string bucket)
    {
        if (!ValidBucketRegex.IsMatch(bucket))
        {
            throw new NatsKVException("Invalid bucket name");
        }
    }

    private static string BucketToStream(string bucket)
        => $"{KvStreamNamePrefix}{bucket}";

    private static string ExtractBucketName(string streamName)
        => streamName.Substring(KvStreamNamePrefixLen);

    private static StreamConfig CreateStreamConfig(NatsKVConfig config)
    {
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
            ? StreamConfigStorage.File
            : StreamConfigStorage.Memory;

        var republish = config.Republish != null
            ? new Republish { Dest = config.Republish.Dest!, Src = config.Republish.Src!, HeadersOnly = config.Republish.HeadersOnly, }
            : null;

        var replicas = config.NumberOfReplicas > 0 ? config.NumberOfReplicas : 1;

        var streamConfig = new StreamConfig
        {
            Name = BucketToStream(config.Bucket),
            Description = config.Description!,
            Subjects = subjects,
            MaxMsgsPerSubject = history,
            MaxBytes = config.MaxBytes,
            MaxAge = config.MaxAge,
            MaxMsgSize = config.MaxValueSize,
            Compression = config.Compression ? StreamConfigCompression.S2 : StreamConfigCompression.None,
            Storage = storage,
            Republish = republish!,
            AllowRollupHdrs = true,
            DenyDelete = true,
            DenyPurge = false,
            AllowDirect = true,
            NumReplicas = replicas,
            Discard = StreamConfigDiscard.New,

            // TODO: KV mirrors
            // MirrorDirect =
            // Mirror =
            Retention = StreamConfigRetention.Limits, // from ADR-8
            DuplicateWindow = config.MaxAge != default ? config.MaxAge : TimeSpan.FromMinutes(2), // 120_000_000_000ns, from ADR-8
        };

        // https://github.com/nats-io/nats.go/blob/98430acd80423b776149f29d625d158f490ac3c5/jetstream/kv.go#L334-L342
        if (streamConfig.MaxAge > TimeSpan.Zero && streamConfig.MaxAge < streamConfig.DuplicateWindow)
            streamConfig.DuplicateWindow = streamConfig.MaxAge;

        return streamConfig;
    }
}
