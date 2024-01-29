using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;
using NATS.Client.ObjectStore.Internal;
using NATS.Client.ObjectStore.Models;

namespace NATS.Client.ObjectStore;

/// <summary>
/// NATS Object Store.
/// </summary>
public class NatsObjStore : INatsObjStore
{
    private const int DefaultChunkSize = 128 * 1024;
    private const string NatsRollup = "Nats-Rollup";
    private const string RollupSubject = "sub";

    private static readonly NatsHeaders NatsRollupHeaders = new() { { NatsRollup, RollupSubject } };

    private readonly NatsObjContext _objContext;
    private readonly NatsJSContext _context;
    private readonly INatsJSStream _stream;

    internal NatsObjStore(NatsObjConfig config, NatsObjContext objContext, NatsJSContext context, INatsJSStream stream)
    {
        Bucket = config.Bucket;
        _objContext = objContext;
        _context = context;
        _stream = stream;
    }

    /// <summary>
    /// Object store bucket name.
    /// </summary>
    public string Bucket { get; }

    /// <summary>
    /// Get object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object value as a byte array.</returns>
    public async ValueTask<byte[]> GetBytesAsync(string key, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        await GetAsync(key, memoryStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Get object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="stream">Stream to write the object value to.</param>
    /// <param name="leaveOpen"><c>true</c> to not close the underlying stream when async method returns; otherwise, <c>false</c></param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsObjException">Metadata didn't match the value retrieved e.g. the SHA digest.</exception>
    public async ValueTask<ObjectMetadata> GetAsync(string key, Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(key);

        var info = await GetInfoAsync(key, cancellationToken: cancellationToken);

        if (info.Options?.Link is { } link)
        {
            var store = await _objContext.GetObjectStoreAsync(link.Bucket, cancellationToken).ConfigureAwait(false);
            return await store.GetAsync(link.Name, stream, leaveOpen, cancellationToken).ConfigureAwait(false);
        }

        if (info.Nuid is null)
        {
            throw new NatsObjException("Object-store meta information invalid");
        }

        await using var pushConsumer = new NatsJSOrderedPushConsumer<NatsMemoryOwner<byte>>(
            context: _context,
            stream: $"OBJ_{Bucket}",
            filter: GetChunkSubject(info.Nuid),
            serializer: NatsDefaultSerializer<NatsMemoryOwner<byte>>.Default,
            opts: new NatsJSOrderedPushConsumerOpts { DeliverPolicy = ConsumerConfigDeliverPolicy.All },
            subOpts: new NatsSubOpts(),
            cancellationToken: cancellationToken);

        pushConsumer.Init();

        string digest;
        var chunks = 0;
        var size = 0;
        using (var sha256 = SHA256.Create())
        {
            await using (var hashedStream = new CryptoStream(stream, sha256, CryptoStreamMode.Write, leaveOpen))
            {
                await foreach (var msg in pushConsumer.Msgs.ReadAllAsync(cancellationToken))
                {
                    // We have to make sure to carry on consuming the channel to avoid any blocking:
                    // e.g. if the channel is full, we would be blocking the reads off the socket (this was intentionally
                    // done ot avoid bloating the memory with a large backlog of messages or dropping messages at this level
                    // and signal the server that we are a slow consumer); then when we make an request-reply API call to
                    // delete the consumer, the socket would be blocked trying to send the response back to us; so we need to
                    // keep consuming the channel to avoid this.
                    if (pushConsumer.IsDone)
                        continue;

                    if (msg.Data.Length > 0)
                    {
                        using var memoryOwner = msg.Data;
                        chunks++;
                        size += memoryOwner.Memory.Length;
                        await hashedStream.WriteAsync(memoryOwner.Memory, cancellationToken);
                    }

                    var p = msg.Metadata?.NumPending;
                    if (p is 0)
                    {
                        pushConsumer.Done();
                    }
                }
            }

            digest = Base64UrlEncoder.Encode(sha256.Hash);
        }

        if ($"SHA-256={digest}" != info.Digest)
        {
            throw new NatsObjException("SHA-256 digest mismatch");
        }

        if (chunks != info.Chunks)
        {
            throw new NatsObjException("Chunks mismatch");
        }

        if (size != info.Size)
        {
            throw new NatsObjException("Size mismatch");
        }

        return info;
    }

    /// <summary>
    /// Put an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="value">Object value as a byte array.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    public ValueTask<ObjectMetadata> PutAsync(string key, byte[] value, CancellationToken cancellationToken = default) =>
        PutAsync(new ObjectMetadata { Name = key }, new MemoryStream(value), cancellationToken: cancellationToken);

    /// <summary>
    /// Put an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="stream">Stream to read the value from.</param>
    /// <param name="leaveOpen"><c>true</c> to not close the underlying stream when async method returns; otherwise, <c>false</c></param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsObjException">There was an error calculating SHA digest.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<ObjectMetadata> PutAsync(string key, Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default) =>
        PutAsync(new ObjectMetadata { Name = key }, stream, leaveOpen, cancellationToken);

    /// <summary>
    /// Put an object by key.
    /// </summary>
    /// <param name="meta">Object metadata.</param>
    /// <param name="stream">Stream to read the value from.</param>
    /// <param name="leaveOpen"><c>true</c> to not close the underlying stream when async method returns; otherwise, <c>false</c></param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsObjException">There was an error calculating SHA digest.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask<ObjectMetadata> PutAsync(ObjectMetadata meta, Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(meta.Name);

        ObjectMetadata? info = null;
        try
        {
            info = await GetInfoAsync(meta.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (NatsObjNotFoundException)
        {
        }

        var nuid = NewNuid();
        meta.Bucket = Bucket;
        meta.Nuid = nuid;
        meta.MTime = DateTimeOffset.UtcNow;

        if (meta.Options == null!)
        {
            meta.Options = new MetaDataOptions { MaxChunkSize = DefaultChunkSize };
        }

        if (meta.Options.MaxChunkSize is null or <= 0)
        {
            meta.Options.MaxChunkSize = DefaultChunkSize;
        }

        var size = 0;
        var chunks = 0;
        var chunkSize = meta.Options.MaxChunkSize.Value;

        string digest;
        using (var sha256 = SHA256.Create())
        {
            await using (var hashedStream = new CryptoStream(stream, sha256, CryptoStreamMode.Read, leaveOpen))
            {
                while (true)
                {
                    var memoryOwner = NatsMemoryOwner<byte>.Allocate(chunkSize);

                    var memory = memoryOwner.Memory;
                    var currentChunkSize = 0;
                    var eof = false;

                    // Fill a chunk
                    while (true)
                    {
                        var read = await hashedStream.ReadAsync(memory, cancellationToken);

                        // End of stream
                        if (read == 0)
                        {
                            eof = true;
                            break;
                        }

                        memory = memory.Slice(read);
                        currentChunkSize += read;

                        // Chunk filled
                        if (memory.IsEmpty)
                        {
                            break;
                        }
                    }

                    if (currentChunkSize > 0)
                    {
                        size += currentChunkSize;
                        chunks++;
                    }

                    var buffer = memoryOwner.Slice(0, currentChunkSize);

                    // Chunks
                    var ack = await _context.PublishAsync(Telemetry.NatsInternalActivities, GetChunkSubject(nuid), buffer, cancellationToken: cancellationToken);
                    ack.EnsureSuccess();

                    if (eof)
                        break;
                }
            }

            if (sha256.Hash == null)
                throw new NatsObjException("Can't compute SHA256 hash");

            digest = Base64UrlEncoder.Encode(sha256.Hash);
        }

        meta.Chunks = chunks;
        meta.Size = size;
        meta.Digest = $"SHA-256={digest}";

        // Metadata
        await PublishMeta(meta, cancellationToken);

        // Delete the old object
        if (info?.Nuid != null && info.Nuid != nuid)
        {
            try
            {
                await _context.JSRequestResponseAsync<StreamPurgeRequest, StreamPurgeResponse>(
                    activitySource: Telemetry.NatsInternalActivities,
                    subject: $"{_context.Opts.Prefix}.STREAM.PURGE.OBJ_{Bucket}",
                    request: new StreamPurgeRequest
                    {
                        Filter = GetChunkSubject(info.Nuid),
                    },
                    cancellationToken);
            }
            catch (NatsJSApiException e)
            {
                if (e.Error.Code != 404)
                    throw;
            }
        }

        return meta;
    }

    /// <summary>
    /// Update object metadata
    /// </summary>
    /// <param name="key">Object key</param>
    /// <param name="meta">Object metadata</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata</returns>
    /// <exception cref="NatsObjException">There is already an object with the same name</exception>
    public async ValueTask<ObjectMetadata> UpdateMetaAsync(string key, ObjectMetadata meta, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(meta.Name);

        var info = await GetInfoAsync(key, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (key != meta.Name)
        {
            // Make sure the new name is available
            try
            {
                await GetInfoAsync(meta.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
                throw new NatsObjException($"Object already exists: {meta.Name}");
            }
            catch (NatsObjNotFoundException)
            {
            }
        }

        info.Name = meta.Name;
        info.Description = meta.Description;
        info.Metadata = meta.Metadata;
        info.Headers = meta.Headers;

        await PublishMeta(info, cancellationToken);

        return info;
    }

    /// <summary>
    /// Add a link to another object
    /// </summary>
    /// <param name="link">Link name</param>
    /// <param name="target">Target object's name</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Metadata of the new link object</returns>
    public ValueTask<ObjectMetadata> AddLinkAsync(string link, string target, CancellationToken cancellationToken = default) =>
        AddLinkAsync(link, new ObjectMetadata { Name = target, Bucket = Bucket }, cancellationToken);

    /// <summary>
    /// Add a link to another object
    /// </summary>
    /// <param name="link">Link name</param>
    /// <param name="target">Target object's metadata</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Metadata of the new link object</returns>
    public async ValueTask<ObjectMetadata> AddLinkAsync(string link, ObjectMetadata target, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(link);
        ValidateObjectName(target.Name);

        if (target.Deleted)
        {
            throw new NatsObjException("Can't link to a deleted object");
        }

        if (target.Bucket is null)
        {
            throw new NatsObjException("Can't link to a target without bucket");
        }

        if (target.Options?.Link is not null)
        {
            throw new NatsObjException("Can't link to a linked object");
        }

        try
        {
            var checkLink = await GetInfoAsync(link, showDeleted: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (checkLink.Options?.Link is null)
            {
                throw new NatsObjException("Object already exists");
            }
        }
        catch (NatsObjNotFoundException)
        {
        }

        var info = new ObjectMetadata
        {
            Name = link,
            Bucket = Bucket,
            Nuid = NewNuid(),
            Options = new MetaDataOptions
            {
                Link = new NatsObjLink
                {
                    Name = target.Name,
                    Bucket = target.Bucket,
                },
            },
        };

        await PublishMeta(info, cancellationToken);

        return info;
    }

    /// <summary>
    /// Add a link to another object store
    /// </summary>
    /// <param name="link">Object's name to be linked</param>
    /// <param name="target">Target object store</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Metadata of the new link object</returns>
    /// <exception cref="NatsObjException">Object with the same name already exists</exception>
    public async ValueTask<ObjectMetadata> AddBucketLinkAsync(string link, INatsObjStore target, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(link);

        try
        {
            var checkLink = await GetInfoAsync(link, showDeleted: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (checkLink.Options?.Link is null)
            {
                throw new NatsObjException("Object already exists");
            }
        }
        catch (NatsObjNotFoundException)
        {
        }

        var info = new ObjectMetadata
        {
            Name = link,
            Bucket = Bucket,
            Nuid = NewNuid(),
            Options = new MetaDataOptions
            {
                Link = new NatsObjLink
                {
                    Name = link,
                    Bucket = target.Bucket,
                },
            },
        };

        await PublishMeta(info, cancellationToken);

        return info;
    }

    /// <summary>
    /// Seal the object store. No further modifications will be allowed.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsObjException">Update operation failed</exception>
    public async ValueTask SealAsync(CancellationToken cancellationToken = default)
    {
        var info = await _context.JSRequestResponseAsync<object, StreamInfoResponse>(
            activitySource: Telemetry.NatsActivities,
            subject: $"{_context.Opts.Prefix}.STREAM.INFO.{_stream.Info.Config.Name}",
            request: null,
            cancellationToken).ConfigureAwait(false);

        var config = info.Config;
        config.Sealed = true;

        var response = await _context.JSRequestResponseAsync<StreamConfig, StreamUpdateResponse>(
            activitySource: Telemetry.NatsActivities,
            subject: $"{_context.Opts.Prefix}.STREAM.UPDATE.{_stream.Info.Config.Name}",
            request: config,
            cancellationToken);

        if (!response.Config.Sealed)
        {
            throw new NatsObjException("Can't seal object store");
        }
    }

    /// <summary>
    /// Get object metadata by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="showDeleted">Also retrieve deleted objects.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsObjException">Object was not found.</exception>
    public async ValueTask<ObjectMetadata> GetInfoAsync(string key, bool showDeleted = false, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(key);

        var request = new StreamMsgGetRequest
        {
            LastBySubj = GetMetaSubject(key),
        };
        try
        {
            var response = await _stream.GetAsync(request, cancellationToken);

            if (response.Message.Data == null)
            {
                throw new NatsObjException("Can't decode data message value");
            }

            var base64String = Convert.FromBase64String(response.Message.Data);
            var data = NatsObjJsonSerializer<ObjectMetadata>.Default.Deserialize(new ReadOnlySequence<byte>(base64String)) ?? throw new NatsObjException("Can't deserialize object metadata");

            if (!showDeleted && data.Deleted)
            {
                throw new NatsObjNotFoundException($"Object not found: {key}");
            }

            return data;
        }
        catch (NatsJSApiException e)
        {
            if (e.Error.Code == 404)
            {
                throw new NatsObjNotFoundException($"Object not found: {key}");
            }

            throw;
        }
    }

    /// <summary>
    /// List all the objects in this store.
    /// </summary>
    /// <param name="opts">List options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>An async enumerable object metadata to be used in an <c>await foreach</c></returns>
    public IAsyncEnumerable<ObjectMetadata> ListAsync(NatsObjListOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= new NatsObjListOpts();
        var watchOpts = new NatsObjWatchOpts
        {
            InitialSetOnly = true,
            UpdatesOnly = false,
            IgnoreDeletes = !opts.ShowDeleted,
        };
        return WatchAsync(watchOpts, cancellationToken);
    }

    /// <summary>
    /// Retrieves run-time status about the backing store of the bucket.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object store status</returns>
    public async ValueTask<NatsObjStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _stream.RefreshAsync(cancellationToken);
        var isCompressed = _stream.Info.Config.Compression != StreamConfigCompression.None;
        return new NatsObjStatus(Bucket, isCompressed, _stream.Info);
    }

    /// <summary>
    /// Watch for changes in the underlying store and receive meta information updates.
    /// </summary>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>An async enumerable object metadata to be used in an <c>await foreach</c></returns>
    public async IAsyncEnumerable<ObjectMetadata> WatchAsync(NatsObjWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= new NatsObjWatchOpts();

        var deliverPolicy = ConsumerConfigDeliverPolicy.All;

        if (!opts.IncludeHistory)
        {
            deliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject;
        }

        if (opts.UpdatesOnly)
        {
            deliverPolicy = ConsumerConfigDeliverPolicy.New;
        }

        await using var pushConsumer = new NatsJSOrderedPushConsumer<NatsMemoryOwner<byte>>(
            context: _context,
            stream: $"OBJ_{Bucket}",
            filter: $"$O.{Bucket}.M.>",
            serializer: NatsDefaultSerializer<NatsMemoryOwner<byte>>.Default,
            opts: new NatsJSOrderedPushConsumerOpts { DeliverPolicy = deliverPolicy },
            subOpts: new NatsSubOpts(),
            cancellationToken: cancellationToken);

        pushConsumer.Init();

        await foreach (var msg in pushConsumer.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pushConsumer.IsDone)
                continue;
            using (msg.Data)
            {
                if (msg.Metadata is { } metadata)
                {
                    var info = JsonSerializer.Deserialize(msg.Data.Memory.Span, NatsObjJsonSerializerContext.Default.ObjectMetadata);
                    if (info != null)
                    {
                        if (!opts.IgnoreDeletes || !info.Deleted)
                        {
                            info.MTime = metadata.Timestamp;
                            yield return info;
                        }
                    }

                    if (opts.InitialSetOnly)
                    {
                        if (metadata.NumPending == 0)
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Delete an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsObjException">Object metadata was invalid or chunks can't be purged.</exception>
    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(key);

        var meta = await GetInfoAsync(key, showDeleted: true, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(meta.Nuid))
        {
            throw new NatsObjException("Object-store meta information invalid");
        }

        meta.Size = 0;
        meta.Chunks = 0;
        meta.Digest = string.Empty;
        meta.Deleted = true;
        meta.MTime = DateTimeOffset.UtcNow;

        await PublishMeta(meta, cancellationToken);

        var response = await _stream.PurgeAsync(new StreamPurgeRequest { Filter = GetChunkSubject(meta.Nuid) }, cancellationToken);
        if (!response.Success)
        {
            throw new NatsObjException("Can't purge object chunks");
        }
    }

    private async ValueTask PublishMeta(ObjectMetadata meta, CancellationToken cancellationToken)
    {
        var ack = await _context.PublishAsync(Telemetry.NatsInternalActivities, GetMetaSubject(meta.Name), meta, serializer: NatsObjJsonSerializer<ObjectMetadata>.Default, headers: NatsRollupHeaders, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }

    private string GetMetaSubject(string key) => $"$O.{Bucket}.M.{Base64UrlEncoder.Encode(key)}";

    private string GetChunkSubject(string nuid) => $"$O.{Bucket}.C.{nuid}";

    private string NewNuid()
    {
        Span<char> buffer = stackalloc char[22];
        if (NuidWriter.TryWriteNuid(buffer))
        {
            return new string(buffer);
        }

        throw new InvalidOperationException("Internal error: can't generate nuid");
    }

    private void ValidateObjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new NatsObjException("Object name can't be empty");
        }
    }
}

public record NatsObjWatchOpts
{
    public bool IgnoreDeletes { get; init; }

    public bool IncludeHistory { get; init; }

    public bool UpdatesOnly { get; init; }

    /// <summary>
    /// Only return the initial set of objects and don't watch for further updates.
    /// </summary>
    public bool InitialSetOnly { get; init; }
}

public record NatsObjListOpts
{
    public bool ShowDeleted { get; init; }
}

public record NatsObjStatus(string Bucket, bool IsCompressed, StreamInfo Info);
