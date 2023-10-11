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
    Delete,

    /// <summary>
    /// A value was purged from a bucket
    /// </summary>
    Purge,
}

/// <summary>
/// Key Value Store
/// </summary>
public class NatsKVStore
{
    private readonly string _bucket;
    private readonly NatsKVOpts _opts;
    private readonly NatsJSContext _context;
    private readonly NatsJSStream _stream;
    private readonly INatsSerializer _serializer;

    internal NatsKVStore(string bucket, NatsKVOpts opts, NatsJSContext context, NatsJSStream stream)
    {
        _bucket = bucket;
        _opts = opts;
        _context = context;
        _stream = stream;
        _serializer = _opts.Serializer ?? _context.Connection.Opts.Serializer;
    }

    /// <summary>
    /// Put a value into the bucket using the key
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    public async ValueTask PutAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var ack = await _context.PublishAsync($"$KV.{_bucket}.{key}", value, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }

    /// <summary>
    /// Get an entry from the bucket using the key
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="serializer">Optional serialized to override the default</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>The entry</returns>
    /// <exception cref="NatsKVException">There was an error with metadata</exception>
    public async ValueTask<NatsKVEntry<T?>> GetEntryAsync<T>(string key, INatsSerializer? serializer = default, CancellationToken cancellationToken = default)
    {
        if (_stream.Info.Config.AllowDirect)
        {
            var direct = await _stream.GetDirectAsync<T>($"$KV.{_bucket}.{key}", serializer ?? _serializer, cancellationToken);
            if (direct is { Headers: { } headers } msg)
            {
                if (!headers.TryGetValue("Nats-Sequence", out var sequenceValues))
                    throw new NatsKVException("Missing sequence header");

                if (sequenceValues.Count != 1)
                    throw new NatsKVException("Unexpected number of sequence headers");

                if (!long.TryParse(sequenceValues[0], out var sequence))
                    throw new NatsKVException("Can't parse sequence header");

                if (!headers.TryGetValue("Nats-Time-Stamp", out var timestampValues))
                    throw new NatsKVException("Missing timestamp header");

                if (timestampValues.Count != 1)
                    throw new NatsKVException("Unexpected number of timestamp headers");

                if (!DateTimeOffset.TryParse(timestampValues[0], out var timestamp))
                    throw new NatsKVException("Can't parse timestamp header");

                var operation = NatsKVOperation.Put;
                if (headers.TryGetValue("KV-Operation", out var operationValues))
                {
                    if (operationValues.Count != 1)
                        throw new NatsKVException("Unexpected number of operation headers");

                    if (!Enum.TryParse(operationValues[0], ignoreCase: true, out operation))
                        throw new NatsKVException("Can't parse operation header");
                }

                return new NatsKVEntry<T?>(_bucket, key)
                {
                    Bucket = _bucket,
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
            var response = await _stream.GetAsync(new StreamMsgGetRequest { LastBySubj = $"$KV.{_bucket}.{key}" }, cancellationToken);

            if (!DateTimeOffset.TryParse(response.Message.Time, out var created))
                throw new NatsKVException("Can't parse timestamp message value");

            T? data;
            var bytes = ArrayPool<byte>.Shared.Rent(_context.Connection.Opts.ReaderBufferSize);
            try
            {
                if (Convert.TryFromBase64String(response.Message.Data, bytes, out var written))
                {
                    var buffer = new ReadOnlySequence<byte>(bytes.AsMemory(0, written));
                    data = (serializer ?? _serializer).Deserialize<T>(buffer);
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

            return new NatsKVEntry<T?>(_bucket, key)
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
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>A watcher object</returns>
    public async ValueTask<INatsKVWatcher<T>> WatchAsync<T>(string key, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= NatsKVWatchOpts.Default;

        var watcher = new NatsKVWatcher<T>(
            context: _context,
            bucket: _bucket,
            key: key,
            opts: opts,
            subOpts: default,
            cancellationToken);

        await watcher.InitAsync();

        return watcher;
    }

    /// <summary>
    /// Start a watcher for all the keys in the bucket
    /// </summary>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>A watcher object</returns>
    public ValueTask<INatsKVWatcher<T>> WatchAsync<T>(NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default) =>
        WatchAsync<T>(">", opts, cancellationToken);

    /// <summary>
    /// Start a watcher for specific keys
    /// </summary>
    /// <param name="key">Key to watch (subject-based wildcards may be used)</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    public async IAsyncEnumerable<NatsKVEntry<T?>> WatchAllAsync<T>(string key, NatsKVWatchOpts? opts = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var watcher = await WatchAsync<T>(key, opts, cancellationToken);

        while (await watcher.Entries.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (watcher.Entries.TryRead(out var entry))
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Start a watcher for all the keys in the bucket
    /// </summary>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    public IAsyncEnumerable<NatsKVEntry<T?>> WatchAllAsync<T>(NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default) =>
        WatchAllAsync<T>(">", opts, cancellationToken);
}
