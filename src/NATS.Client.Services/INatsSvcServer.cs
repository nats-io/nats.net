using NATS.Client.Core;
using NATS.Client.Services.Models;

namespace NATS.Client.Services;

/// <summary>
/// NATS Services interface.
/// </summary>
public interface INatsSvcServer : IAsyncDisposable
{
    /// <summary>
    /// Stop the service.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the stop operation.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new endpoint.
    /// </summary>
    /// <param name="handler">Callback for handling incoming messages.</param>
    /// <param name="name">Optional endpoint name.</param>
    /// <param name="subject">Optional endpoint subject.</param>
    /// <param name="queueGroup">Queue group name (defaults to service group's).</param>
    /// <param name="metadata">Optional endpoint metadata.</param>
    /// <param name="serializer">Serializer to use when deserializing incoming messages (defaults to connection's serializer).</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to stop the endpoint.</param>
    /// <typeparam name="T">Serialization type for messages received.</typeparam>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// One of name or subject must be specified.
    /// </remarks>
    ValueTask AddEndpointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, string? queueGroup = default, IDictionary<string, string>? metadata = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an endpoint by name, draining and disposing it.
    /// </summary>
    /// <param name="name">Name of the endpoint to remove.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to bound the drain operation's PING/PONG fence</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// The endpoint stops receiving new messages, then any messages it has already received are
    /// handled before it is disposed. Other endpoints on this service are unaffected.
    /// <para>
    /// An endpoint is named by the <c>name</c> it was added with. An endpoint added without a
    /// name takes its name from its subject with dots replaced by dashes, and for an endpoint
    /// added through a <see cref="NatsSvcServer.Group"/> that subject includes the group prefix.
    /// So on a group <c>g1</c>, an endpoint added as <c>name: "e1"</c> is removed as <c>e1</c>,
    /// but one added as <c>subject: "s1"</c> with no name is removed as <c>g1-s1</c>.
    /// </para>
    /// </remarks>
    ValueTask RemoveEndpointAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new service group with optional queue group.
    /// </summary>
    /// <param name="name">Name of the group.</param>
    /// <param name="queueGroup">Queue group name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> may be used to cancel th call in the future.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask<NatsSvcServer.Group> AddGroupAsync(string name, string? queueGroup = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current stats for the service.
    /// </summary>
    /// <returns>Stats response object</returns>
    StatsResponse GetStats();

    /// <summary>
    /// Get current info for the service.
    /// </summary>
    /// <returns>Info response object</returns>
    InfoResponse GetInfo();
}
