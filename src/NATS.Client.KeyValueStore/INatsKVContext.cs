using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore;

public interface INatsKVContext
{
    /// <summary>
    /// Provides access to the JetStream context associated with the Key-Value Store operations.
    /// </summary>
    INatsJSContext JetStreamContext { get; }

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsKVStore> CreateStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsKVStore> CreateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucjet</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsKVStore> GetStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a key value store configuration. Storage type cannot change.
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken"> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsKVStore> UpdateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Key Value Store if it doesn't exist or update if the store already exists.
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<INatsKVStore> CreateOrUpdateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>True for success</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    ValueTask<bool> DeleteStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of bucket names
    /// </summary>
    /// <param name="cancellationToken"> used to cancel the API call.</param>
    /// <returns>Async enumerable of bucket names. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    IAsyncEnumerable<string> GetBucketNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status for all buckets
    /// </summary>
    /// <param name="cancellationToken"> used to cancel the API call.</param>
    /// <returns>Async enumerable of Key/Value statuses. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    IAsyncEnumerable<NatsKVStatus> GetStatusesAsync(CancellationToken cancellationToken = default);
}
