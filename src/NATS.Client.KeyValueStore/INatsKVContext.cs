namespace NATS.Client.KeyValueStore;

public interface INatsKVContext
{
    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    ValueTask<INatsKVStore> CreateStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new Key Value Store or get an existing one
    /// </summary>
    /// <param name="config">Key Value Store configuration</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    ValueTask<INatsKVStore> CreateStoreAsync(NatsKVConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucjet</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Key Value Store</returns>
    /// <exception cref="NatsKVException">There was an issue with configuration</exception>
    ValueTask<INatsKVStore> GetStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a Key Value Store
    /// </summary>
    /// <param name="bucket">Name of the bucket</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>True for success</returns>
    ValueTask<bool> DeleteStoreAsync(string bucket, CancellationToken cancellationToken = default);
}
