using System.Text.RegularExpressions;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.ObjectStore;

/// <summary>
/// Object Store context.
/// </summary>
public class NatsObjContext : INatsObjContext
{
    private static readonly Regex ValidBucketRegex = new(pattern: @"\A[a-zA-Z0-9_-]+\z", RegexOptions.Compiled);

    private readonly NatsJSContext _context;

    /// <summary>
    /// Create a new object store context.
    /// </summary>
    /// <param name="context">JetStream context.</param>
    public NatsObjContext(NatsJSContext context) => _context = context;

    /// <summary>
    /// Create a new object store.
    /// </summary>
    /// <param name="bucket">Bucket name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object store object.</returns>
    public ValueTask<INatsObjStore> CreateObjectStore(string bucket, CancellationToken cancellationToken = default) =>
        CreateObjectStore(new NatsObjConfig(bucket), cancellationToken);

    /// <summary>
    /// Create a new object store.
    /// </summary>
    /// <param name="config">Object store configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object store object.</returns>
    public async ValueTask<INatsObjStore> CreateObjectStore(NatsObjConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var storage = config.Storage == NatsObjStorageType.File
            ? StreamConfigurationStorage.file
            : StreamConfigurationStorage.memory;

        var streamConfiguration = new StreamConfiguration
        {
            Name = $"OBJ_{config.Bucket}",
            Description = config.Description!,
            Subjects = new[] { $"$O.{config.Bucket}.C.>", $"$O.{config.Bucket}.M.>" },
            MaxAge = config.MaxAge?.ToNanos() ?? 0,
            MaxBytes = config.MaxBytes ?? -1,
            Storage = storage,
            NumReplicas = config.NumberOfReplicas,
            /* TODO: Placement = */
            Discard = StreamConfigurationDiscard.@new,
            AllowRollupHdrs = true,
            AllowDirect = true,
            Metadata = config.Metadata!,
            Retention = StreamConfigurationRetention.limits,
            Compression = config.IsCompressed ? StreamConfigurationCompression.s2 : StreamConfigurationCompression.none,
        };

        var stream = await _context.CreateStreamAsync(streamConfiguration, cancellationToken);
        return new NatsObjStore(config, this, _context, stream);
    }

    /// <summary>
    /// Get an existing object store.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The Object Store object</returns>
    public async ValueTask<INatsObjStore> GetObjectStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);
        var stream = await _context.GetStreamAsync($"OBJ_{bucket}", cancellationToken: cancellationToken);
        return new NatsObjStore(new NatsObjConfig(bucket), this, _context, stream);
    }

    /// <summary>
    /// Delete an object store.
    /// </summary>
    /// <param name="bucket">Name of the bucket.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    public ValueTask<bool> DeleteObjectStore(string bucket, CancellationToken cancellationToken)
    {
        ValidateBucketName(bucket);
        return _context.DeleteStreamAsync($"OBJ_{bucket}", cancellationToken);
    }

    private void ValidateBucketName(string bucket)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new NatsObjException("Bucket name can't be empty");
        }

        if (bucket.StartsWith("."))
        {
            throw new NatsObjException("Bucket name can't start with a period");
        }

        if (bucket.EndsWith("."))
        {
            throw new NatsObjException("Bucket name can't end with a period");
        }

        if (!ValidBucketRegex.IsMatch(bucket))
        {
            throw new NatsObjException("Bucket name can only contain alphanumeric characters, dashes, and underscores");
        }
    }
}
