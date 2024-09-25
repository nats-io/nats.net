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

    /// <summary>
    /// Create a new object store context.
    /// </summary>
    /// <param name="context">JetStream context.</param>
    public NatsObjContext(INatsJSContext context) => JetStreamContext = context;

    /// <inheritdoc />
    public INatsJSContext JetStreamContext { get; }

    /// <inheritdoc />
    public ValueTask<INatsObjStore> CreateObjectStoreAsync(string bucket, CancellationToken cancellationToken = default) =>
        CreateObjectStoreAsync(new NatsObjConfig(bucket), cancellationToken);

    /// <inheritdoc />
    public async ValueTask<INatsObjStore> CreateObjectStoreAsync(NatsObjConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var storage = config.Storage == NatsObjStorageType.File
            ? StreamConfigStorage.File
            : StreamConfigStorage.Memory;

        var streamConfig = new StreamConfig
        {
            Name = $"OBJ_{config.Bucket}",
            Description = config.Description!,
            Subjects = new[] { $"$O.{config.Bucket}.C.>", $"$O.{config.Bucket}.M.>" },
            MaxAge = config.MaxAge ?? TimeSpan.Zero,
            MaxBytes = config.MaxBytes ?? -1,
            Storage = storage,
            NumReplicas = config.NumberOfReplicas,
            /* TODO: Placement = */
            Discard = StreamConfigDiscard.New,
            AllowRollupHdrs = true,
            AllowDirect = true,
            Metadata = config.Metadata!,
            Retention = StreamConfigRetention.Limits,
            Compression = config.Compression ? StreamConfigCompression.S2 : StreamConfigCompression.None,
        };

        var stream = await JetStreamContext.CreateStreamAsync(streamConfig, cancellationToken);
        return new NatsObjStore(config, this, JetStreamContext, stream);
    }

    /// <inheritdoc />
    public async ValueTask<INatsObjStore> GetObjectStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);
        var stream = await JetStreamContext.GetStreamAsync($"OBJ_{bucket}", cancellationToken: cancellationToken);
        return new NatsObjStore(new NatsObjConfig(bucket), this, JetStreamContext, stream);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteObjectStore(string bucket, CancellationToken cancellationToken)
    {
        ValidateBucketName(bucket);
        return JetStreamContext.DeleteStreamAsync($"OBJ_{bucket}", cancellationToken);
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
