using System.Buffers;
using System.Runtime.CompilerServices;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore.Internal;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Key Value Store entry operation
/// </summary>
public enum NatsKVOperation
{
    /// <summary>
    /// A value was put into the bucket
    /// </summary>
    Put,

    /// <summary>
    /// A value was deleted from a bucket
    /// </summary>
    Del,

    /// <summary>
    /// A value was purged from a bucket
    /// </summary>
    Purge,
}

/// <summary>
/// Key Value Store
/// </summary>
public class NatsKVStore : INatsKVStore
{
    private const string NatsExpectedLastSubjectSequence = "Nats-Expected-Last-Subject-Sequence";
    private const string KVOperation = "KV-Operation";
    private const string NatsRollup = "Nats-Rollup";
    private const string OperationPurge = "PURGE";
    private const string RollupSub = "sub";
    private const string OperationDel = "DEL";
    private const string NatsSubject = "Nats-Subject";
    private const string NatsSequence = "Nats-Sequence";
    private const string NatsTimeStamp = "Nats-Time-Stamp";
    private readonly NatsJSContext _context;
    private readonly INatsJSStream _stream;

    internal NatsKVStore(string bucket, NatsJSContext context, INatsJSStream stream)
    {
        Bucket = bucket;
        _context = context;
        _stream = stream;
    }

    public string Bucket { get; }

    /// <inheritdoc />
    public async ValueTask<ulong> PutAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var ack = await _context.PublishAsync($"$KV.{Bucket}.{key}", value, serializer: serializer, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
        return ack.Seq;
    }

    /// <inheritdoc />
    public async ValueTask<ulong> CreateAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        // First try to create a new entry
        try
        {
            return await UpdateAsync(key, value, revision: 0, serializer, cancellationToken);
        }
        catch (NatsKVWrongLastRevisionException)
        {
        }

        // If that fails, try to update an existing entry which may have been deleted
        try
        {
            await GetEntryAsync<T>(key, cancellationToken: cancellationToken);
        }
        catch (NatsKVKeyDeletedException e)
        {
            return await UpdateAsync(key, value, e.Revision, serializer, cancellationToken);
        }

        throw new NatsKVCreateException();
    }

    /// <inheritdoc />
    public async ValueTask<ulong> UpdateAsync<T>(string key, T value, ulong revision, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var headers = new NatsHeaders { { NatsExpectedLastSubjectSequence, revision.ToString() } };

        try
        {
            var ack = await _context.PublishAsync($"$KV.{Bucket}.{key}", value, headers: headers, serializer: serializer, cancellationToken: cancellationToken);
            ack.EnsureSuccess();

            return ack.Seq;
        }
        catch (NatsJSApiException e)
        {
            if (e.Error is { ErrCode: 10071, Code: 400, Description: not null } && e.Error.Description.StartsWith("wrong last sequence", StringComparison.OrdinalIgnoreCase))
            {
                throw new NatsKVWrongLastRevisionException();
            }

            throw;
        }
    }

    public async ValueTask DeleteAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= new NatsKVDeleteOpts();

        var headers = new NatsHeaders();

        if (opts.Purge)
        {
            headers.Add(KVOperation, OperationPurge);
            headers.Add(NatsRollup, RollupSub);
        }
        else
        {
            headers.Add(KVOperation, OperationDel);
        }

        if (opts.Revision != default)
        {
            headers.Add(NatsExpectedLastSubjectSequence, opts.Revision.ToString());
        }

        var subject = $"$KV.{Bucket}.{key}";

        try
        {
            var ack = await _context.PublishAsync<object?>(subject, null, headers: headers, cancellationToken: cancellationToken);
            ack.EnsureSuccess();
        }
        catch (NatsJSApiException e)
        {
            if (e.Error is { ErrCode: 10071, Code: 400, Description: not null } && e.Error.Description.StartsWith("wrong last sequence", StringComparison.OrdinalIgnoreCase))
            {
                throw new NatsKVWrongLastRevisionException();
            }

            throw;
        }
    }

    public ValueTask PurgeAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default) =>
        DeleteAsync(key, (opts ?? new NatsKVDeleteOpts()) with { Purge = true }, cancellationToken);

    /// <summary>
    /// Get an entry from the bucket using the key
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="revision">Revision to retrieve</param>
    /// <param name="serializer">Optional serialized to override the default</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>The entry</returns>
    /// <exception cref="NatsKVException">There was an error with metadata</exception>
    public async ValueTask<NatsKVEntry<T>> GetEntryAsync<T>(string key, ulong revision = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        var request = new StreamMsgGetRequest();
        var keySubject = $"$KV.{Bucket}.{key}";

        if (revision == default)
        {
            request.LastBySubj = keySubject;
        }
        else
        {
            request.Seq = revision;
            request.NextBySubj = keySubject;
        }

        if (_stream.Info.Config.AllowDirect)
        {
            var direct = await _stream.GetDirectAsync<T>(request, serializer, cancellationToken);

            if (direct is { Headers: { } headers } msg)
            {
                if (headers.Code == 404)
                    throw new NatsKVKeyNotFoundException();

                if (!headers.TryGetValue(NatsSubject, out var subjectValues))
                    throw new NatsKVException("Missing sequence header");

                var subject = subjectValues[^1];

                if (revision != default)
                {
                    if (!string.Equals(subject, keySubject, StringComparison.Ordinal))
                    {
                        throw new NatsKVException("Unexpected subject");
                    }
                }

                if (!headers.TryGetValue(NatsSequence, out var sequenceValues))
                    throw new NatsKVException("Missing sequence header");

                if (sequenceValues.Count != 1)
                    throw new NatsKVException("Unexpected number of sequence headers");

                if (!ulong.TryParse(sequenceValues[0], out var sequence))
                    throw new NatsKVException("Can't parse sequence header");

                if (!headers.TryGetValue(NatsTimeStamp, out var timestampValues))
                    throw new NatsKVException("Missing timestamp header");

                if (timestampValues.Count != 1)
                    throw new NatsKVException("Unexpected number of timestamp headers");

                if (!DateTimeOffset.TryParse(timestampValues[0], out var timestamp))
                    throw new NatsKVException("Can't parse timestamp header");

                var operation = NatsKVOperation.Put;
                if (headers.TryGetValue(KVOperation, out var operationValues))
                {
                    if (operationValues.Count != 1)
                        throw new NatsKVException("Unexpected number of operation headers");

                    if (!Enum.TryParse(operationValues[0], ignoreCase: true, out operation))
                        throw new NatsKVException("Can't parse operation header");
                }

                if (operation is NatsKVOperation.Del or NatsKVOperation.Purge)
                {
                    throw new NatsKVKeyDeletedException(sequence);
                }

                return new NatsKVEntry<T>(Bucket, key)
                {
                    Bucket = Bucket,
                    Key = key,
                    Created = timestamp,
                    Revision = sequence,
                    Operation = operation,
                    Value = msg.Data,
                    Delta = 0,
                    UsedDirectGet = true,
                };
            }
            else
            {
                throw new NatsKVException("Missing headers");
            }
        }
        else
        {
            var response = await _stream.GetAsync(request, cancellationToken);

            if (revision != default)
            {
                if (string.Equals(response.Message.Subject, keySubject, StringComparison.Ordinal))
                {
                    throw new NatsKVException("Unexpected subject");
                }
            }

            if (!DateTimeOffset.TryParse(response.Message.Time, out var created))
                throw new NatsKVException("Can't parse timestamp message value");

            T? data;
            var bytes = ArrayPool<byte>.Shared.Rent(_context.Connection.Opts.ReaderBufferSize);
            try
            {
                if (response.Message.Data != null && Convert.TryFromBase64String(response.Message.Data, bytes, out var written))
                {
                    var buffer = new ReadOnlySequence<byte>(bytes.AsMemory(0, written));
                    data = serializer.Deserialize(buffer);
                }
                else
                {
                    throw new NatsKVException("Can't decode data message value");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            return new NatsKVEntry<T>(Bucket, key)
            {
                Created = created,
                Revision = response.Message.Seq,
                Value = data,
                UsedDirectGet = false,
            };
        }
    }

    /// <summary>
    /// Start a watcher for specific keys
    /// </summary>
    /// <param name="key">Key to watch (subject-based wildcards may be used)</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    public async IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(string key, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var watcher = await WatchInternalAsync<T>(key, serializer, opts, cancellationToken);

        while (await watcher.Entries.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (watcher.Entries.TryRead(out var entry))
            {
                yield return entry;
            }
        }
    }

    public async IAsyncEnumerable<NatsKVEntry<T>> HistoryAsync<T>(string key, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            await GetEntryAsync<T>(key, cancellationToken: cancellationToken);
        }
        catch (NatsKVKeyNotFoundException)
        {
            yield break;
        }
        catch (NatsKVKeyDeletedException)
        {
        }

        opts ??= NatsKVWatchOpts.Default;
        opts = opts with { IncludeHistory = true };

        await using var watcher = await WatchInternalAsync<T>(key, serializer, opts, cancellationToken);

        while (await watcher.Entries.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (watcher.Entries.TryRead(out var entry))
            {
                yield return entry;
                if (entry.Delta == 0)
                    yield break;
            }
        }
    }

    public async ValueTask<NatsKVStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _stream.RefreshAsync(cancellationToken);
        var isCompressed = _stream.Info.Config.Compression != StreamConfigCompression.None;
        return new NatsKVStatus(Bucket, isCompressed, _stream.Info);
    }

    /// <summary>
    /// Start a watcher for all the keys in the bucket
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    public IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default) =>
        WatchAsync<T>(">", serializer, opts, cancellationToken);

    public async ValueTask PurgeDeletesAsync(NatsKVPurgeOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVPurgeOpts.Default;

        var limit = DateTimeOffset.UtcNow - opts.DeleteMarkersThreshold;

        var timeLimited = opts.DeleteMarkersThreshold > TimeSpan.Zero;

        var deleted = new List<NatsKVEntry<int>>();

        // Type doesn't matter here, we're just using the watcher to get the keys
        await using (var watcher = await WatchInternalAsync<int>(">", opts: new NatsKVWatchOpts { MetaOnly = true }, cancellationToken: cancellationToken))
        {
            while (await watcher.Entries.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (watcher.Entries.TryRead(out var entry))
                {
                    if (entry.Operation is NatsKVOperation.Purge or NatsKVOperation.Del)
                        deleted.Add(entry);
                    if (entry.Delta == 0)
                        goto PURGE_LOOP_DONE;
                }
            }
        }

    PURGE_LOOP_DONE:

        foreach (var entry in deleted)
        {
            var request = new StreamPurgeRequest { Filter = $"$KV.{Bucket}.{entry.Key}" };

            if (timeLimited && entry.Created > limit)
            {
                request.Keep = 1;
            }

            var response = await _stream.PurgeAsync(request, cancellationToken);
            if (!response.Success)
            {
                throw new NatsKVException("Purge failed");
            }
        }
    }

    public async IAsyncEnumerable<string> GetKeysAsync(NatsKVWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVWatchOpts.Default;

        opts = opts with
        {
            IgnoreDeletes = false,
            MetaOnly = true,
            UpdatesOnly = false,
        };

        // Type doesn't matter here, we're just using the watcher to get the keys
        await using var watcher = await WatchInternalAsync<int>(">", serializer: default, opts, cancellationToken);

        while (await watcher.Entries.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (watcher.Entries.TryRead(out var entry))
            {
                if (entry.Operation is NatsKVOperation.Put)
                    yield return entry.Key;
                if (entry.Delta == 0)
                    yield break;
            }
        }
    }

    internal async ValueTask<NatsKVWatcher<T>> WatchInternalAsync<T>(string key, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVWatchOpts.Default;
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        var watcher = new NatsKVWatcher<T>(
            context: _context,
            bucket: Bucket,
            key: key,
            opts: opts,
            serializer: serializer,
            subOpts: default,
            cancellationToken: cancellationToken);

        await watcher.InitAsync();

        return watcher;
    }
}

public record NatsKVStatus(string Bucket, bool IsCompressed, StreamInfo Info);
