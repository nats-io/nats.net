namespace NATS.Client.ObjectStore;

/// <summary>
/// NATS Object Store context.
/// </summary>
public interface INatsObjContext
{
    /// <summary>
    /// Create a new object store.
    /// </summary>
    /// <param name="bucket">Bucket name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object store object.</returns>
    ValueTask<INatsObjStore> CreateObjectStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new object store.
    /// </summary>
    /// <param name="config">Object store configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object store object.</returns>
    ValueTask<INatsObjStore> CreateObjectStoreAsync(NatsObjConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an existing object store.
    /// </summary>
    /// <param name="bucket">Bucket name</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The Object Store object</returns>
    ValueTask<INatsObjStore> GetObjectStoreAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an object store.
    /// </summary>
    /// <param name="bucket">Name of the bucket.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    ValueTask<bool> DeleteObjectStore(string bucket, CancellationToken cancellationToken);
}
