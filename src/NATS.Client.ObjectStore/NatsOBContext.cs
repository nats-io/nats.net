using System.Text.RegularExpressions;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.ObjectStore;

public class NatsOBContext
{
    private static readonly Regex ValidBucketRegex = new(pattern: @"\A[a-zA-Z0-9_-]+\z", RegexOptions.Compiled);

    private readonly NatsJSContext _context;

    public NatsOBContext(NatsJSContext context)
    {
        _context = context;
    }

    public async ValueTask<NatsOBStore> CreateObjectStore(NatsOBConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var storage = config.Storage == NatsOBStorageType.File
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
        };

        var stream = await _context.CreateStreamAsync(streamConfiguration, cancellationToken);
        return new NatsOBStore(config, _context, stream);
    }

    private void ValidateBucketName(string bucket)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new NatsOBException("Bucket name can't be empty");
        }

        if (bucket.StartsWith("."))
        {
            throw new NatsOBException("Bucket name can't start with a period");
        }

        if (bucket.EndsWith("."))
        {
            throw new NatsOBException("Bucket name can't end with a period");
        }

        if (!ValidBucketRegex.IsMatch(bucket))
        {
            throw new NatsOBException("Bucket name can only contain alphanumeric characters, dashes, and underscores");
        }
    }
}
