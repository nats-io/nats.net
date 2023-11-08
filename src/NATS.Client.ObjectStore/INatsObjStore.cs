using NATS.Client.JetStream;
using NATS.Client.ObjectStore.Models;

namespace NATS.Client.ObjectStore;

/// <summary>
/// NATS Object Store.
/// </summary>
public interface INatsObjStore
{
    /// <summary>
    /// Object store bucket name.
    /// </summary>
    string Bucket { get; }

    /// <summary>
    /// Get object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object value as a byte array.</returns>
    ValueTask<byte[]> GetBytesAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="stream">Stream to write the object value to.</param>
    /// <param name="leaveOpen"><c>true</c> to not close the underlying stream when async method returns; otherwise, <c>false</c></param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsObjException">Metadata didn't match the value retrieved e.g. the SHA digest.</exception>
    ValueTask<ObjectMetadata> GetAsync(string key, Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Put an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="value">Object value as a byte array.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    ValueTask<ObjectMetadata> PutAsync(string key, byte[] value, CancellationToken cancellationToken = default);

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
    ValueTask<ObjectMetadata> PutAsync(string key, Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default);

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
    ValueTask<ObjectMetadata> PutAsync(ObjectMetadata meta, Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update object metadata
    /// </summary>
    /// <param name="key">Object key</param>
    /// <param name="meta">Object metadata</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata</returns>
    /// <exception cref="NatsObjException">There is already an object with the same name</exception>
    ValueTask<ObjectMetadata> UpdateMetaAsync(string key, ObjectMetadata meta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a link to another object
    /// </summary>
    /// <param name="link">Link name</param>
    /// <param name="target">Target object's name</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Metadata of the new link object</returns>
    ValueTask<ObjectMetadata> AddLinkAsync(string link, string target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a link to another object
    /// </summary>
    /// <param name="link">Link name</param>
    /// <param name="target">Target object's metadata</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Metadata of the new link object</returns>
    ValueTask<ObjectMetadata> AddLinkAsync(string link, ObjectMetadata target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a link to another object store
    /// </summary>
    /// <param name="link">Object's name to be linked</param>
    /// <param name="target">Target object store</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Metadata of the new link object</returns>
    /// <exception cref="NatsObjException">Object with the same name already exists</exception>
    ValueTask<ObjectMetadata> AddBucketLinkAsync(string link, INatsObjStore target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seal the object store. No further modifications will be allowed.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsObjException">Update operation failed</exception>
    ValueTask SealAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get object metadata by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="showDeleted">Also retrieve deleted objects.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object metadata.</returns>
    /// <exception cref="NatsObjException">Object was not found.</exception>
    ValueTask<ObjectMetadata> GetInfoAsync(string key, bool showDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all the objects in this store.
    /// </summary>
    /// <param name="opts">List options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>An async enumerable object metadata to be used in an <c>await foreach</c></returns>
    IAsyncEnumerable<ObjectMetadata> ListAsync(NatsObjListOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves run-time status about the backing store of the bucket.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Object store status</returns>
    ValueTask<NatsObjStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Watch for changes in the underlying store and receive meta information updates.
    /// </summary>
    /// <param name="opts">Watch options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>An async enumerable object metadata to be used in an <c>await foreach</c></returns>
    IAsyncEnumerable<ObjectMetadata> WatchAsync(NatsObjWatchOpts? opts = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an object by key.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsObjException">Object metadata was invalid or chunks can't be purged.</exception>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);
}
