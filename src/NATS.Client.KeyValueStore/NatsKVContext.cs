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

    /// <summary>
    /// Create a new Key Value Store context
    /// </summary>
    /// <param name="context">JetStream context</param>
    public NatsKVContext(INatsJSContext context) => JetStreamContext = context;

    /// <inheritdoc />
    public INatsJSContext JetStreamContext { get; }

    /// <inheritdoc />
    public ValueTask<INatsKVStore> CreateStoreAsync(string bucket, CancellationToken cancellationToken = default)
        => CreateStoreAsync(new NatsKVConfig(bucket), cancellationToken);

    /// <inheritdoc />
    public async ValueTask<INatsKVStore> CreateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var streamConfig = NatsKVContext.CreateStreamConfig(config);

        var stream = await JetStreamContext.CreateStreamAsync(streamConfig, cancellationToken);

        return new NatsKVStore(config.Bucket, JetStreamContext, stream);
    }

    /// <inheritdoc />
    public async ValueTask<INatsKVStore> GetStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);

        var stream = await JetStreamContext.GetStreamAsync(BucketToStream(bucket), cancellationToken: cancellationToken);

        if (stream.Info.Config.MaxMsgsPerSubject < 1)
        {
            throw new NatsKVException("Invalid KV store name");
        }

        // TODO: KV mirror
        return new NatsKVStore(bucket, JetStreamContext, stream);
    }

    /// <inheritdoc />
    public async ValueTask<INatsKVStore> UpdateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var streamConfig = NatsKVContext.CreateStreamConfig(config);

        var stream = await JetStreamContext.UpdateStreamAsync(streamConfig, cancellationToken);

        return new NatsKVStore(config.Bucket, JetStreamContext, stream);
    }

    /// <inheritdoc />
    public async ValueTask<INatsKVStore> CreateOrUpdateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(config.Bucket);

        var streamConfig = NatsKVContext.CreateStreamConfig(config);

        var stream = await JetStreamContext.CreateOrUpdateStreamAsync(streamConfig, cancellationToken);

        return new NatsKVStore(config.Bucket, JetStreamContext, stream);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteStoreAsync(string bucket, CancellationToken cancellationToken = default)
    {
        ValidateBucketName(bucket);
        return JetStreamContext.DeleteStreamAsync(BucketToStream(bucket), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GetBucketNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var name in JetStreamContext.ListStreamNamesAsync(cancellationToken: cancellationToken))
        {
            if (!name.StartsWith(KvStreamNamePrefix))
            {
                continue;
            }

            yield return ExtractBucketName(name);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NatsKVStatus> GetStatusesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var name in JetStreamContext.ListStreamNamesAsync(cancellationToken: cancellationToken))
        {
            var stream = await JetStreamContext.GetStreamAsync(name, cancellationToken: cancellationToken);
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

        string[]? subjects;
        StreamSource? mirror;
        ICollection<StreamSource>? sources;
        bool mirrorDirect;

        if (config.Mirror != null)
        {
            mirror = config.Mirror with
            {
                Name = config.Mirror.Name.StartsWith(KvStreamNamePrefix)
                    ? config.Mirror.Name
                    : BucketToStream(config.Mirror.Name),
            };
            mirrorDirect = true;
            subjects = default;
            sources = default;
        }
        else if (config.Sources is { Count: > 0 })
        {
            sources = [];
            foreach (var ss in config.Sources)
            {
                string? sourceBucketName;
                if (ss.Name.StartsWith(KvStreamNamePrefix))
                {
                    sourceBucketName = ss.Name.Substring(KvStreamNamePrefixLen);
                }
                else
                {
                    sourceBucketName = ss.Name;
                    ss.Name = BucketToStream(ss.Name);
                }

                if (ss.External == null || sourceBucketName != config.Bucket)
                {
                    ss.SubjectTransforms =
                    [
                        new SubjectTransform
                        {
                            Src = $"$KV.{sourceBucketName}.>",
                            Dest = $"$KV.{config.Bucket}.>",
                        }
                    ];
                }

                sources.Add(ss);
            }

            subjects = [$"$KV.{config.Bucket}.>"];
            mirror = default;
            mirrorDirect = false;
        }
        else
        {
            subjects = [$"$KV.{config.Bucket}.>"];
            mirror = default;
            sources = default;
            mirrorDirect = false;
        }

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
            Placement = config.Placement,
            AllowRollupHdrs = true,
            DenyDelete = true,
            DenyPurge = false,
            AllowDirect = true,
            NumReplicas = replicas,
            Discard = StreamConfigDiscard.New,
            Mirror = mirror,
            MirrorDirect = mirrorDirect,
            Sources = sources,
            Retention = StreamConfigRetention.Limits, // from ADR-8
            AllowMsgTTL = config.AllowMsgTTL,
            SubjectDeleteMarkerTTL = config.SubjectDeleteMarkerTTL,
        };

        return streamConfig;
    }
}
