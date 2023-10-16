using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
public class NatsOBStore
{
    private const int DefaultChunkSize = 128 * 1024;
    private const string NatsRollup = "Nats-Rollup";
    private const string RollupSubject = "sub";

    private static readonly NatsHeaders NatsRollupHeaders = new() { { NatsRollup, RollupSubject } };
    private static readonly Regex ValidObjectRegex = new(pattern: @"\A[-/_=\.a-zA-Z0-9]+\z", RegexOptions.Compiled);

    private readonly string _bucket;
    private readonly NatsJSContext _context;
    private readonly NatsJSStream _stream;

    internal NatsOBStore(NatsOBConfig config, NatsJSContext context, NatsJSStream stream)
    {
        _bucket = config.Bucket;
        _context = context;
        _stream = stream;
    }

    /// <summary>
    /// Get object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object value as a byte array.</returns>
    public async ValueTask<byte[]> GetBytesAsync(string key, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        await GetAsync(key, memoryStream, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Get object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="stream">Stream to write the object value to.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsOBException">Metadata didn't match the value retrieved e.g. the SHA digest.</exception>
    public async ValueTask<ObjectMetadata> GetAsync(string key, Stream stream, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(key);

        var info = await GetInfoAsync(key, cancellationToken: cancellationToken);

        await using var pushConsumer = new NatsJSOrderedPushConsumer<IMemoryOwner<byte>>(
            _context,
            $"OBJ_{_bucket}",
            GetChunkSubject(info.Nuid),
            new NatsJSOrderedPushConsumerOpts { DeliverPolicy = ConsumerConfigurationDeliverPolicy.all },
            new NatsSubOpts(),
            cancellationToken);

        pushConsumer.Init();

        string digest;
        var chunks = 0;
        var size = 0;
        using (var sha256 = SHA256.Create())
        {
            await using (var hashedStream = new CryptoStream(stream, sha256, CryptoStreamMode.Write))
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

                    if (msg.Data != null)
                    {
                        using (msg.Data)
                        {
                            chunks++;
                            size += msg.Data.Memory.Length;
                            await hashedStream.WriteAsync(msg.Data.Memory, cancellationToken);
                        }
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
            throw new NatsOBException("SHA-256 digest mismatch");
        }

        if (chunks != info.Chunks)
        {
            throw new NatsOBException("Chunks mismatch");
        }

        if (size != info.Size)
        {
            throw new NatsOBException("Size mismatch");
        }

        await stream.FlushAsync(cancellationToken);

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
        PutAsync(new ObjectMetadata { Name = key }, new MemoryStream(value), cancellationToken);

    /// <summary>
    /// Put an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="stream">Stream to read the value from.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsOBException">There was an error calculating SHA digest.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<ObjectMetadata> PutAsync(string key, Stream stream, CancellationToken cancellationToken = default) =>
        PutAsync(new ObjectMetadata { Name = key }, stream, cancellationToken);

    /// <summary>
    /// Put an object by key.
    /// </summary>
    /// <param name="meta">Object metadata.</param>
    /// <param name="stream">Stream to read the value from.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsOBException">There was an error calculating SHA digest.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask<ObjectMetadata> PutAsync(ObjectMetadata meta, Stream stream, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(meta.Name);

        ObjectMetadata? info = null;
        try
        {
            info = await GetInfoAsync(meta.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (NatsJSApiException e)
        {
            if (e.Error.Code != 404)
                throw;
        }

        var nuid = NewNuid();
        meta.Bucket = _bucket;
        meta.Nuid = nuid;
        meta.MTime = DateTimeOffset.UtcNow;

        if (meta.Options == null!)
        {
            meta.Options = new Options { MaxChunkSize = DefaultChunkSize };
        }

        if (meta.Options.MaxChunkSize == 0)
        {
            meta.Options.MaxChunkSize = DefaultChunkSize;
        }

        var size = 0;
        var chunks = 0;
        var chunkSize = meta.Options.MaxChunkSize;

        string digest;
        using (var sha256 = SHA256.Create())
        {
            await using (var hashedStream = new CryptoStream(stream, sha256, CryptoStreamMode.Read))
            {
                while (true)
                {
                    var memoryOwner = new FixedSizeMemoryOwner(MemoryPool<byte>.Shared.Rent(chunkSize), chunkSize);

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

                    var buffer = new FixedSizeMemoryOwner(memoryOwner, currentChunkSize);

                    // Chunks
                    var ack = await _context.PublishAsync(GetChunkSubject(nuid), buffer, cancellationToken: cancellationToken);
                    ack.EnsureSuccess();

                    if (eof)
                        break;
                }
            }

            if (sha256.Hash == null)
                throw new NatsOBException("Can't compute SHA256 hash");

            digest = Base64UrlEncoder.Encode(sha256.Hash);
        }

        meta.Chunks = chunks;
        meta.Size = size;
        meta.Digest = $"SHA-256={digest}";

        // Metadata
        await PublishMeta(meta, cancellationToken);

        // Delete the old object
        if (info != null && info.Nuid != nuid)
        {
            try
            {
                await _context.JSRequestResponseAsync<StreamPurgeRequest, StreamPurgeResponse>(
                    subject: $"{_context.Opts.Prefix}.STREAM.PURGE.OBJ_{_bucket}",
                    request: new StreamPurgeRequest { Filter = GetChunkSubject(info.Nuid) },
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
    /// Get object metadata by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="showDeleted">Also retrieve deleted objects.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsOBException">Object was not found.</exception>
    public async ValueTask<ObjectMetadata> GetInfoAsync(string key, bool showDeleted = false, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(key);

        var request = new StreamMsgGetRequest { LastBySubj = GetMetaSubject(key) };

        var response = await _stream.GetAsync(request, cancellationToken);

        var data = NatsJsonSerializer.Default.Deserialize<ObjectMetadata>(new ReadOnlySequence<byte>(Convert.FromBase64String(response.Message.Data))) ?? throw new NatsOBException("Can't deserialize object metadata");

        if (!showDeleted && data.Deleted)
        {
            throw new NatsOBException("Object not found");
        }

        return data;
    }

    /// <summary>
    /// Delete an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsOBException">Object metadata was invalid or chunks can't be purged.</exception>
    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(key);

        var meta = await GetInfoAsync(key, showDeleted: true, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(meta.Nuid))
        {
            throw new NatsOBException("Object-store meta information invalid");
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
            throw new NatsOBException("Can't purge object chunks");
        }
    }

    private async ValueTask PublishMeta(ObjectMetadata meta, CancellationToken cancellationToken)
    {
        var ack = await _context.PublishAsync(GetMetaSubject(meta.Name), meta, headers: NatsRollupHeaders, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }

    private string GetMetaSubject(string key) => $"$O.{_bucket}.M.{Base64UrlEncoder.Encode(key)}";

    private string GetChunkSubject(string nuid) => $"$O.{_bucket}.C.{nuid}";

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
            throw new NatsOBException("Object name can't be empty");
        }

        if (!ValidObjectRegex.IsMatch(name))
        {
            throw new NatsOBException("Object name can only contain alphanumeric characters, dashes, underscores, forward slash, equals sign, and periods");
        }
    }
}
