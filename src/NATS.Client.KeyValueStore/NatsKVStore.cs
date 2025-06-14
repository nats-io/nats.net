using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    internal const string NatsMarkerReason = "Nats-Marker-Reason";
    private const string NatsExpectedLastSubjectSequence = "Nats-Expected-Last-Subject-Sequence";
    private const string KVOperation = "KV-Operation";
    private const string NatsRollup = "Nats-Rollup";
    private const string OperationPurge = "PURGE";
    private const string RollupSub = "sub";
    private const string OperationDel = "DEL";
    private const string NatsSubject = "Nats-Subject";
    private const string NatsSequence = "Nats-Sequence";
    private const string NatsTimeStamp = "Nats-Time-Stamp";
    private const string NatsTTL = "Nats-TTL";
    private static readonly Regex ValidKeyRegex = new(pattern: @"\A[-/_=\.a-zA-Z0-9]+\z", RegexOptions.Compiled);
    private static readonly NatsKVException MissingSequenceHeaderException = new("Missing sequence header");
    private static readonly NatsKVException MissingTimestampHeaderException = new("Missing timestamp header");
    private static readonly NatsKVException MissingHeadersException = new("Missing headers");
    private static readonly NatsKVException UnexpectedSubjectException = new("Unexpected subject");
    private static readonly NatsKVException UnexpectedNumberOfOperationHeadersException = new("Unexpected number of operation headers");
    private static readonly NatsKVException InvalidSequenceException = new("Can't parse sequence header");
    private static readonly NatsKVException InvalidTimestampException = new("Can't parse timestamp header");
    private static readonly NatsKVException InvalidOperationException = new("Can't parse operation header");
    private static readonly NatsKVException KeyCannotBeEmptyException = new("Key cannot be empty");
    private static readonly NatsKVException KeyCannotStartOrEndWithPeriodException = new("Key cannot start or end with a period");
    private static readonly NatsKVException KeyContainsInvalidCharactersException = new("Key contains invalid characters");
    private static readonly NatsKVException ThisStoreDoesNotSupportTTLException = new("This store does not support TTL");
    private readonly INatsJSStream _stream;
    private readonly NatsKVOpts _opts;
    private readonly string _kvBucket;
    private readonly string _streamName;
    private bool _supportsTTL;

    internal NatsKVStore(string bucket, INatsJSContext context, INatsJSStream stream, NatsKVOpts opts)
    {
        Bucket = bucket;
        JetStreamContext = context;
        _stream = stream;
        _opts = opts;
        _kvBucket = $"$KV.{Bucket}.";
        _streamName = NatsKVContext.KvStreamNamePrefix + Bucket;
        _supportsTTL = GetLimitMarkerTTL(stream.Info.Config) > TimeSpan.Zero;
    }

    /// <inheritdoc />
    public INatsJSContext JetStreamContext { get; }

    /// <inheritdoc />
    public string Bucket { get; }

    /// <summary>
    /// <para>
    /// Tests for a valid Bucket Key
    /// </para>
    /// <para>
    /// Valid keys are \A[-/_=\.a-zA-Z0-9]+\z, additionally they may not start or end in .
    /// </para>
    /// </summary>
    /// <param name="key">Subject to publish the data to.</param>
    /// <returns>
    /// A NatsResult signifying if the key is Valid, or if invalid, the exception detail.
    /// </returns>
    public static NatsResult IsValidKey(string key) => TryValidateKey(key);

    /// <inheritdoc />
    public ValueTask<ulong> PutAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default) => PutAsync<T>(key, value, default, serializer, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ulong> PutAsync<T>(string key, T value, TimeSpan ttl = default, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var result = await TryPutAsync(key, value, ttl, serializer, cancellationToken);
        if (!result.Success)
        {
            ThrowException(result.Error);
        }

        return result.Value;
    }

    /// <inheritdoc />
    public ValueTask<NatsResult<ulong>> TryPutAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default) => TryPutAsync<T>(key, value, default, serializer, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<NatsResult<ulong>> TryPutAsync<T>(string key, T value, TimeSpan ttl = default, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var keyValidResult = TryValidateKey(key);
        if (!keyValidResult.Success)
        {
            return keyValidResult.Error;
        }

        NatsHeaders? headers = default;
        if (ttl != default)
        {
            headers = new NatsHeaders
            {
                { NatsTTL, ToTTLString(ttl) },
            };
        }

        var publishResult = await JetStreamContext.TryPublishAsync(_kvBucket + key, value, serializer: serializer, headers: headers, cancellationToken: cancellationToken);
        if (publishResult.Success)
        {
            var ack = publishResult.Value;
            if (ack.Error != null)
            {
                return new NatsJSApiException(ack.Error);
            }
            else if (ack.Duplicate)
            {
                return new NatsJSDuplicateMessageException(ack.Seq);
            }

            return ack.Seq;
        }
        else
        {
            return publishResult.Error;
        }
    }

    /// <inheritdoc />
    public ValueTask<ulong> CreateAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default) => CreateAsync<T>(key, value, default, serializer, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ulong> CreateAsync<T>(string key, T value, TimeSpan ttl = default, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var result = await TryCreateAsync(key, value, ttl, serializer, cancellationToken);
        if (!result.Success)
        {
            ThrowException(result.Error);
        }

        return result.Value;
    }

    /// <inheritdoc />
    public ValueTask<NatsResult<ulong>> TryCreateAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
        => TryCreateAsync<T>(key, value, TimeSpan.Zero, serializer, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<NatsResult<ulong>> TryCreateAsync<T>(string key, T value, TimeSpan ttl, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        if (ttl > TimeSpan.Zero && !_supportsTTL)
        {
            return ThisStoreDoesNotSupportTTLException;
        }

        var keyValidResult = TryValidateKey(key);
        if (!keyValidResult.Success)
        {
            return keyValidResult.Error;
        }

        // First try to create a new entry
        var resultUpdate = await TryUpdateInternalAsync(key, value, revision: 0, ttl, serializer, cancellationToken);
        if (resultUpdate.Success)
        {
            return resultUpdate;
        }

        // If that fails, try to read an existing entry, this will fail if deleted.
        var resultReadExisting = await TryGetEntryAsync<T>(key, cancellationToken: cancellationToken);

        // If we succeed here, then we've just been returned an entry, so we can't create a new one
        if (resultReadExisting.Success)
        {
            return new NatsKVCreateException();
        }
        else if (resultReadExisting.Error is NatsKVKeyDeletedException deletedException)
        {
            // If our previous call errored because the last entry is deleted, then that's ok, we update with the deleted revision
            return await TryUpdateInternalAsync(key, value, deletedException.Revision, ttl, serializer, cancellationToken);
        }
        else
        {
            return resultReadExisting.Error;
        }
    }

    /// <inheritdoc />
    public ValueTask<ulong> UpdateAsync<T>(string key, T value, ulong revision, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default) => UpdateAsync(key, value, revision, default, serializer, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ulong> UpdateAsync<T>(string key, T value, ulong revision, TimeSpan ttl = default, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var result = await TryUpdateInternalAsync(key, value, revision, ttl, serializer, cancellationToken);
        if (!result.Success)
        {
            ThrowException(result.Error);
        }

        return result.Value;
    }

    /// <inheritdoc />
    public ValueTask<NatsResult<ulong>> TryUpdateAsync<T>(string key, T value, ulong revision, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default) =>
        TryUpdateInternalAsync(key, value, revision, TimeSpan.Zero, serializer, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NatsResult<ulong>> TryUpdateAsync<T>(string key, T value, ulong revision, TimeSpan ttl, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default) =>
        TryUpdateInternalAsync(key, value, revision, ttl, serializer, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DeleteAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var result = await TryDeleteAsync(key, opts, cancellationToken);
        if (!result.Success)
        {
            ThrowException(result.Error);
        }
    }

    /// <inheritdoc />
    public ValueTask<NatsResult> TryDeleteAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default)
        => TryDeleteInternalAsync(key, TimeSpan.Zero, opts, cancellationToken);

    /// <inheritdoc />
    public ValueTask PurgeAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default) =>
        DeleteAsync(key, (opts ?? new NatsKVDeleteOpts()) with { Purge = true }, cancellationToken);

    /// <inheritdoc />
    public async ValueTask PurgeAsync(string key, TimeSpan ttl, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var result = await TryDeleteInternalAsync(key, ttl, (opts ?? new NatsKVDeleteOpts()) with { Purge = true }, cancellationToken);
        if (!result.Success)
        {
            ThrowException(result.Error);
        }
    }

    /// <inheritdoc />
    public ValueTask<NatsResult> TryPurgeAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default) =>
        TryDeleteAsync(key, (opts ?? new NatsKVDeleteOpts()) with { Purge = true }, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NatsResult> TryPurgeAsync(string key, TimeSpan ttl, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default) =>
        TryDeleteInternalAsync(key, ttl, (opts ?? new NatsKVDeleteOpts()) with { Purge = true }, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<NatsKVEntry<T>> GetEntryAsync<T>(string key, ulong revision = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var result = await TryGetEntryAsync(key, revision, serializer, cancellationToken);
        if (!result.Success)
        {
            ThrowException(result.Error);
        }

        return result.Value;
    }

    /// <inheritdoc />
#if !NETSTANDARD
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<NatsResult<NatsKVEntry<T>>> TryGetEntryAsync<T>(string key, ulong revision = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var keyValidResult = TryValidateKey(key);
        if (!keyValidResult.Success)
        {
            return keyValidResult.Error;
        }

        serializer ??= JetStreamContext.Connection.Opts.SerializerRegistry.GetDeserializer<T>();
        var keySubject = _kvBucket + key;

        var request = new StreamMsgGetRequest();
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
            NatsMsg<T> direct;
            if (_opts.UseDirectGetApiWithKeysInSubject)
            {
                direct = await JetStreamContext.Connection.RequestAsync<object, T>(
                    subject: $"{JetStreamContext.Opts.Prefix}.DIRECT.GET.{_streamName}.{keySubject}",
                    data: null,
                    replySerializer: serializer,
                    cancellationToken: cancellationToken);
            }
            else
            {
                direct = await _stream.GetDirectAsync<T>(request, serializer, cancellationToken);
            }

            if (direct is { Headers: { } headers } msg)
            {
                if (headers.Code == 404)
                    return NatsKVKeyNotFoundException.Default;

                if (!headers.TryGetLastValue(NatsSubject, out var subject))
                    return MissingSequenceHeaderException;

                if (revision != default)
                {
                    if (!string.Equals(subject, keySubject, StringComparison.Ordinal))
                    {
                        return UnexpectedSubjectException;
                    }
                }

                if (!headers.TryGetLastValue(NatsSequence, out var sequenceValue))
                    return MissingSequenceHeaderException;

                if (!ulong.TryParse(sequenceValue, out var sequence))
                    return InvalidSequenceException;

                if (!headers.TryGetLastValue(NatsTimeStamp, out var timestampValue))
                    return MissingTimestampHeaderException;

                if (!DateTimeOffset.TryParse(timestampValue, out var timestamp))
                    return InvalidTimestampException;

                var operation = NatsKVOperation.Put;
                if (headers.TryGetValue(KVOperation, out var operationValues))
                {
                    if (operationValues.Count != 1)
                        return UnexpectedNumberOfOperationHeadersException;

                    if (!Enum.TryParse(operationValues[0], ignoreCase: true, out operation))
                        return InvalidOperationException;
                }
                else if (headers.TryGetValue(NatsMarkerReason, out var markerReasonValues))
                {
                    var reason = markerReasonValues.Last();
                    if (reason is "MaxAge" or "Purge")
                    {
                        operation = NatsKVOperation.Purge;
                    }
                    else if (reason is "Remove")
                    {
                        operation = NatsKVOperation.Del;
                    }
                    else
                    {
                        return InvalidOperationException;
                    }
                }

                if (operation is NatsKVOperation.Del or NatsKVOperation.Purge)
                {
                    return new NatsKVKeyDeletedException(sequence);
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
                    Error = msg.Error,
                };
            }
            else
            {
                return MissingHeadersException;
            }
        }
        else
        {
            var response = await _stream.GetAsync(request, cancellationToken);

            if (revision != default)
            {
                if (string.Equals(response.Message.Subject, keySubject, StringComparison.Ordinal))
                {
                    return UnexpectedSubjectException;
                }
            }

            T? data;
            NatsDeserializeException? deserializeException = null;
            if (response.Message.Data.Length > 0)
            {
                var buffer = new ReadOnlySequence<byte>(response.Message.Data);

                try
                {
                    data = serializer.Deserialize(buffer);
                }
                catch (Exception e)
                {
                    deserializeException = new NatsDeserializeException(buffer.ToArray(), e);
                    data = default;
                }
            }
            else
            {
                data = default;
            }

            return new NatsKVEntry<T>(Bucket, key)
            {
                Created = response.Message.Time,
                Revision = response.Message.Seq,
                Value = data,
                UsedDirectGet = false,
                Error = deserializeException,
            };
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(string key, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default)
        => WatchAsync<T>([key], serializer, opts, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(IEnumerable<string> keys, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var watcher = await WatchInternalAsync<T>(keys, serializer, opts, cancellationToken);

        if (watcher.InitialConsumer.Info.NumPending == 0 && opts?.OnNoData != null)
        {
            if (await opts.OnNoData(cancellationToken))
            {
                yield break;
            }
        }

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    /// <inheritdoc />
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

        await using var watcher = await WatchInternalAsync<T>([key], serializer, opts, cancellationToken);

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
            if (entry.Delta == 0)
                yield break;
        }
    }

    /// <inheritdoc />
    public async ValueTask<NatsKVStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _stream.RefreshAsync(cancellationToken);
        var isCompressed = _stream.Info.Config.Compression != StreamConfigCompression.None;
        var limitMarkerTTL = GetLimitMarkerTTL(_stream.Info.Config);
        _supportsTTL = limitMarkerTTL > TimeSpan.Zero;
        return new NatsKVStatus(Bucket, isCompressed, limitMarkerTTL, _stream.Info);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default) =>
        WatchAsync<T>([">"], serializer, opts, cancellationToken);

    /// <inheritdoc />
    public async ValueTask PurgeDeletesAsync(NatsKVPurgeOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVPurgeOpts.Default;

        var limit = DateTimeOffset.UtcNow - opts.DeleteMarkersThreshold;

        var timeLimited = opts.DeleteMarkersThreshold > TimeSpan.Zero;

        var deleted = new List<NatsKVEntry<int>>();

        // Type doesn't matter here, we're just using the watcher to get the keys
        await using (var watcher = await WatchInternalAsync<int>([">"], opts: new NatsKVWatchOpts { MetaOnly = true }, cancellationToken: cancellationToken))
        {
            if (watcher.InitialConsumer.Info.NumPending == 0)
                return;

            await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (entry.Operation is NatsKVOperation.Purge or NatsKVOperation.Del)
                    deleted.Add(entry);
                if (entry.Delta == 0)
                    goto PURGE_LOOP_DONE;
            }
        }

    PURGE_LOOP_DONE:

        foreach (var entry in deleted)
        {
            var request = new StreamPurgeRequest { Filter = _kvBucket + entry.Key };

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

    /// <inheritdoc />
    public IAsyncEnumerable<string> GetKeysAsync(NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default)
        => GetKeysAsync([">"], opts, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GetKeysAsync(IEnumerable<string> filters, NatsKVWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVWatchOpts.Default;

        opts = opts with { IgnoreDeletes = false, MetaOnly = true, UpdatesOnly = false, };

        // Type doesn't matter here, we're just using the watcher to get the keys
        await using var watcher = await WatchInternalAsync<int>(filters, serializer: default, opts, cancellationToken);

        if (watcher.InitialConsumer.Info.NumPending == 0)
            yield break;

        await foreach (var entry in watcher.Entries.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (entry.Operation is NatsKVOperation.Put)
                yield return entry.Key;
            if (entry.Delta == 0)
                yield break;
        }
    }

    internal static TimeSpan GetLimitMarkerTTL(StreamConfig config)
    {
        if (config.AllowMsgTTL)
        {
            return config.SubjectDeleteMarkerTTL;
        }

        return TimeSpan.Zero;
    }

    internal async ValueTask<NatsKVWatcher<T>> WatchInternalAsync<T>(IEnumerable<string> keys, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVWatchOpts.Default;
        serializer ??= JetStreamContext.Connection.Opts.SerializerRegistry.GetDeserializer<T>();

        opts.ThrowIfInvalid();

        var watcher = new NatsKVWatcher<T>(
            context: JetStreamContext,
            bucket: Bucket,
            keys: keys,
            opts: opts,
            serializer: serializer,
            subOpts: default,
            cancellationToken: cancellationToken);

        await watcher.InitAsync();

        return watcher;
    }

    /// <summary>
    /// For the TTL header, we need to convert the TimeSpan to a Go time.ParseDuration string.
    /// </summary>
    /// <param name="ttl">TTL</param>
    /// <returns>String representing the number of seconds Go time.ParseDuration() can understand.</returns>
    private static string ToTTLString(TimeSpan ttl)
        => ttl == TimeSpan.MaxValue ? "never" : $"{(int)ttl.TotalSeconds:D}s";

    /// <summary>
    /// Valid keys are \A[-/_=\.a-zA-Z0-9]+\z, additionally they may not start or end in .
    /// </summary>
    private static NatsResult TryValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length == 0)
        {
            return KeyCannotBeEmptyException;
        }

        if (key[0] == '.' || key[^1] == '.')
        {
            return KeyCannotStartOrEndWithPeriodException;
        }

        if (!ValidKeyRegex.IsMatch(key))
        {
            return KeyContainsInvalidCharactersException;
        }

        return NatsResult.Default;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowException(Exception exception) => throw exception;

    private async ValueTask<NatsResult<ulong>> TryUpdateInternalAsync<T>(string key, T value, ulong revision, TimeSpan ttl, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        var keyValidResult = TryValidateKey(key);
        if (!keyValidResult.Success)
        {
            return keyValidResult.Error;
        }

        var headers = new NatsHeaders { { NatsExpectedLastSubjectSequence, revision.ToString() } };
        if (ttl > TimeSpan.Zero)
        {
            headers.Add(NatsTTL, ToTTLString(ttl));
        }

        var publishResult = await JetStreamContext.TryPublishAsync(_kvBucket + key, value, headers: headers, serializer: serializer, cancellationToken: cancellationToken);
        if (publishResult.Success)
        {
            var ack = publishResult.Value;
            if (ack.Error is { ErrCode: 10071, Code: 400, Description: not null } && ack.Error.Description.StartsWith("wrong last sequence", StringComparison.OrdinalIgnoreCase))
            {
                return new NatsKVWrongLastRevisionException();
            }
            else if (ack.Error != null)
            {
                return new NatsJSApiException(ack.Error);
            }
            else if (ack.Duplicate)
            {
                return new NatsJSDuplicateMessageException(ack.Seq);
            }

            return ack.Seq;
        }
        else
        {
            return publishResult.Error;
        }
    }

    private async ValueTask<NatsResult> TryDeleteInternalAsync(string key, TimeSpan ttl, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default)
    {
        var keyValidResult = TryValidateKey(key);
        if (!keyValidResult.Success)
        {
            return keyValidResult.Error;
        }

        opts ??= new NatsKVDeleteOpts();

        var headers = new NatsHeaders();

        if (opts.Purge)
        {
            headers.Add(KVOperation, OperationPurge);
            headers.Add(NatsRollup, RollupSub);
            if (ttl > TimeSpan.Zero)
            {
                if (!_supportsTTL)
                {
                    return ThisStoreDoesNotSupportTTLException;
                }

                headers.Add(NatsTTL, ToTTLString(ttl));
            }
        }
        else
        {
            headers.Add(KVOperation, OperationDel);
        }

        if (opts.Revision != default)
        {
            headers.Add(NatsExpectedLastSubjectSequence, opts.Revision.ToString());
        }

        var publishResult = await JetStreamContext.TryPublishAsync<object?>(_kvBucket + key, null, headers: headers, cancellationToken: cancellationToken);
        if (publishResult.Success)
        {
            var ack = publishResult.Value;
            if (ack.Error is { ErrCode: 10071, Code: 400, Description: not null } && ack.Error.Description.StartsWith("wrong last sequence", StringComparison.OrdinalIgnoreCase))
            {
                return new NatsKVWrongLastRevisionException();
            }
            else if (ack.Error != null)
            {
                return new NatsJSApiException(ack.Error);
            }
            else if (ack.Duplicate)
            {
                return new NatsJSDuplicateMessageException(ack.Seq);
            }

            return NatsResult.Default;
        }
        else
        {
            return publishResult.Error;
        }
    }
}

public record NatsKVStatus(string Bucket, bool IsCompressed, TimeSpan LimitMarkerTTL, StreamInfo Info);
