using NATS.Client.Core;
using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Represents a NATS Key-Value Store bucket.
/// </summary>
/// <seealso href="https://www.nuget.org/packages/Synadia.Orbit.KeyValueStore.Extensions">KV codec extensions for typed serialization</seealso>
public interface INatsKVStore
{
    /// <summary>
    /// Provides access to the JetStream context associated with the Object Store operations.
    /// </summary>
    INatsJSContext JetStreamContext { get; }

    /// <summary>
    /// Name of the Key Value Store bucket
    /// </summary>
    string Bucket { get; }

    /// <summary>
    /// Put a value into the bucket using the key
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>Revision number</returns>
    ValueTask<ulong> PutAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to put a value into the bucket using the key
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>Revision number</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult<ulong>> TryPutAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new entry in the bucket only if it doesn't exist
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>The revision number of the entry</returns>
    ValueTask<ulong> CreateAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new entry in the bucket only if it doesn't exist
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="ttl">Time to live for the entry (requires the <see cref="NatsKVConfig.LimitMarkerTTL"/> to be set to true). For a key that should never expire, use the <see cref="TimeSpan.MaxValue"/> constant. This feature is only available on NATS server v2.11 and later.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>The revision number of the entry</returns>
    ValueTask<ulong> CreateAsync<T>(string key, T value, TimeSpan ttl, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to create a new entry in the bucket only if it doesn't exist
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>A NatsResult object representing the revision number of the created entry or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult<ulong>> TryCreateAsync<T>(string key, T value, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to create a new entry in the bucket only if it doesn't exist
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="ttl">Time to live for the entry (requires the <see cref="NatsKVConfig.LimitMarkerTTL"/> to be set to true). For a key that should never expire, use the <see cref="TimeSpan.MaxValue"/> constant. This feature is only available on NATS server v2.11 and later.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>A NatsResult object representing the revision number of the created entry or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult<ulong>> TryCreateAsync<T>(string key, T value, TimeSpan ttl, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an entry in the bucket only if last update revision matches
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="revision">Last revision number to match</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>The revision number of the updated entry</returns>
    ValueTask<ulong> UpdateAsync<T>(string key, T value, ulong revision, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to update an entry in the bucket only if last update revision matches
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="value">Value of the entry</param>
    /// <param name="revision">Last revision number to match</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>A NatsResult object representing the revision number of the updated entry or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult<ulong>> TryUpdateAsync<T>(string key, T value, ulong revision, INatsSerialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entry from the bucket
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="opts">Delete options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    ValueTask DeleteAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an entry from the bucket
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="opts">Delete options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>A NatsResult object representing success or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult> TryDeleteAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge an entry from the bucket
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="opts">Delete options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    ValueTask PurgeAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge an entry from the bucket
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="ttl">Time to live for the purge marker (requires the <see cref="NatsKVConfig.LimitMarkerTTL"/> to be set to true). For a key that should never expire, use the <see cref="TimeSpan.MaxValue"/> constant. This feature is only available on NATS server v2.11 and later.</param>
    /// <param name="opts">Delete options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    ValueTask PurgeAsync(string key, TimeSpan ttl, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to purge an entry from the bucket
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="opts">Delete options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>A NatsResult object representing success or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult> TryPurgeAsync(string key, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to purge an entry from the bucket
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="ttl">Time to live for the purge marker (requires the <see cref="NatsKVConfig.LimitMarkerTTL"/> to be set to true). For a key that should never expire, use the <see cref="TimeSpan.MaxValue"/> constant. This feature is only available on NATS server v2.11 and later.</param>
    /// <param name="opts">Delete options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>A NatsResult object representing success or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions
    /// </remarks>
    ValueTask<NatsResult> TryPurgeAsync(string key, TimeSpan ttl, NatsKVDeleteOpts? opts = default, CancellationToken cancellationToken = default);

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
    ValueTask<NatsKVEntry<T>> GetEntryAsync<T>(string key, ulong revision = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Try to get an entry from the bucket using the key.
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="revision">Revision to retrieve</param>
    /// <param name="serializer">Optional serialized to override the default</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>A NatsResult object representing the value or an error.</returns>
    /// <remarks>
    /// Use this method to avoid exceptions when, for example, the key is not found.
    /// </remarks>
    ValueTask<NatsResult<NatsKVEntry<T>>> TryGetEntryAsync<T>(string key, ulong revision = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a watcher for specific keys
    /// </summary>
    /// <param name="key">Key to watch (subject-based wildcards may be used)</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(string key, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a watcher for specific keys
    /// </summary>
    /// <param name="keys">Keys to watch (subject-based wildcards may be used)</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    /// <exception cref="InvalidOperationException">There was a conflict in options, e.g. IncludeHistory and UpdatesOnly are only valid when ResumeAtRevision is not set.</exception>
    IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(IEnumerable<string> keys, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a watcher for all the keys in the bucket
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An asynchronous enumerable which can be used in <c>await foreach</c> loops</returns>
    IAsyncEnumerable<NatsKVEntry<T>> WatchAsync<T>(INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the history of an entry by key
    /// </summary>
    /// <param name="key">Key of the entry</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="T">Serialized value type</typeparam>
    /// <returns>An async enumerable of entries to be used in an <c>await foreach</c></returns>
    /// <exception cref="InvalidOperationException">There was a conflict in options, e.g. IncludeHistory and UpdatesOnly are only valid when ResumeAtRevision is not set.</exception>
    IAsyncEnumerable<NatsKVEntry<T>> HistoryAsync<T>(string key, INatsDeserialize<T>? serializer = default, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the bucket status
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The Key/Value store status</returns>
    ValueTask<NatsKVStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge all deleted entries
    /// </summary>
    /// <param name="opts">Purge options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    ValueTask PurgeDeletesAsync(NatsKVPurgeOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all the keys in the bucket
    /// </summary>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>An async enumerable of keys to be used in an <c>await foreach</c></returns>
    /// <exception cref="InvalidOperationException">There was a conflict in options, e.g. IncludeHistory and UpdatesOnly are only valid when ResumeAtRevision is not set.</exception>
    IAsyncEnumerable<string> GetKeysAsync(NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a filtered set of keys in the bucket
    /// </summary>
    /// <param name="filters">Subject-based wildcard filters to filter on</param>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>An async enumerable of keys to be used in an <c>await foreach</c></returns>
    /// <exception cref="InvalidOperationException">There was a conflict in options, e.g. IncludeHistory and UpdatesOnly are only valid when ResumeAtRevision is not set.</exception>
    IAsyncEnumerable<string> GetKeysAsync(IEnumerable<string> filters, NatsKVWatchOpts? opts = default, CancellationToken cancellationToken = default);
}
