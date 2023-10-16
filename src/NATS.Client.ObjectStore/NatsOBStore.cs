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

public class NatsOBStore
{
    private const int DefaultChunkSize = 128 * 1024;
    private const string NatsRollup = "Nats-Rollup";
    private const string RollupSubject = "sub";

    private static readonly NatsHeaders NatsRollupHeaders = new() { { NatsRollup, RollupSubject } };
    private static readonly Regex ValidObjectRegex = new(pattern: @"\A[-/_=\.a-zA-Z0-9]+\z", RegexOptions.Compiled);

    private readonly string _bucket;
    private readonly NatsOBConfig _config;
    private readonly NatsJSContext _context;
    private readonly NatsJSStream _stream;
    private readonly NatsConnection _nats;

    internal NatsOBStore(NatsOBConfig config, NatsJSContext context, NatsJSStream stream)
    {
        _bucket = config.Bucket;
        _config = config;
        _context = context;
        _nats = context.Connection;
        _stream = stream;
    }

    public async ValueTask<ObjectMetadata> GetAsync(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(name);

        var info = await GetInfoAsync(name, cancellationToken);

        await using var pushConsumer = new NatsJSOrderedPushConsumer<IMemoryOwner<byte>>(
            _context,
            $"OBJ_{_bucket}",
            $"$O.{_bucket}.C.{info.Nuid}",
            new NatsJSOrderedPushConsumerOpts { DeliverPolicy = ConsumerConfigurationDeliverPolicy.all },
            new NatsSubOpts(),
            cancellationToken);

        pushConsumer.Init();

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
                    await stream.WriteAsync(msg.Data.Memory, cancellationToken);
                }
            }

            var p = msg.Metadata?.NumPending;
            if (p is 0)
            {
                pushConsumer.Done();
            }
        }

        await stream.FlushAsync(cancellationToken);

        return info;
    }

    public async ValueTask<ObjectMetadata> PutAsync(ObjectMetadata meta, Stream stream, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(meta.Name);

        ObjectMetadata? info = null;
        try
        {
            info = await GetInfoAsync(meta.Name, cancellationToken);
        }
        catch (NatsJSApiException e)
        {
            if (e.Error.Code != 404)
                throw;
        }

        var nuid = NewNuid();
        var encodedName = Base64UrlEncoder.Encode(meta.Name);

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
                    var ack1 = await _context.PublishAsync($"$O.{_bucket}.C.{nuid}", buffer, cancellationToken: cancellationToken);
                    ack1.EnsureSuccess();

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
        var ack2 = await _context.PublishAsync($"$O.{_bucket}.M.{encodedName}", meta, headers: NatsRollupHeaders, cancellationToken: cancellationToken);
        ack2.EnsureSuccess();

        // Delete the old object
        if (info != null && info.Nuid != nuid)
        {
            try
            {
                await _context.JSRequestResponseAsync<StreamPurgeRequest, StreamPurgeResponse>(
                    subject: $"{_context.Opts.Prefix}.STREAM.PURGE.OBJ_{_bucket}",
                    request: new StreamPurgeRequest { Filter = $"$O.{_bucket}.C.{info.Nuid}" },
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

    public async ValueTask<ObjectMetadata> GetInfoAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new StreamMsgGetRequest { LastBySubj = $"$O.{_bucket}.M.{Base64UrlEncoder.Encode(key)}", };

        var response = await _stream.GetAsync(request, cancellationToken);

        var data = NatsJsonSerializer.Default.Deserialize<ObjectMetadata>(new ReadOnlySequence<byte>(Convert.FromBase64String(response.Message.Data))) ?? throw new NatsOBException("Can't deserialize object metadata");

        return data;
    }

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
