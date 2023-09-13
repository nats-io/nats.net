using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// Represents a NATS JetStream stream.
/// </summary>
public class NatsJSStream
{
    private readonly NatsJSContext _context;
    private readonly string _name;
    private bool _deleted;

    internal NatsJSStream(NatsJSContext context, StreamInfo info)
    {
        _context = context;
        Info = info;
        _name = info.Config.Name;
    }

    /// <summary>
    /// Stream info object as retrieved from NATS JetStream server at the time this object was created, updated or refreshed.
    /// </summary>
    public StreamInfo Info { get; private set; }

    /// <summary>
    /// Delete this stream.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>After deletion this object can't be used anymore.</remarks>
    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _deleted = await _context.DeleteStreamAsync(_name, cancellationToken);
    }

    /// <summary>
    /// Update stream properties on the server.
    /// </summary>
    /// <param name="request">Stream update request to be sent to the server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask UpdateAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        var response = await _context.UpdateStreamAsync(request, cancellationToken);
        Info = response.Info;
    }

    /// <summary>
    /// Creates new consumer for this stream if it doesn't exists or returns an existing one with the same name.
    /// </summary>
    /// <param name="consumer">Name of the consumer.</param>
    /// <param name="ackPolicy">Ack policy to use. Must not be set to <c>none</c>. Default is <c>explicit</c>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<NatsJSConsumer> CreateConsumerAsync(string consumer, ConsumerConfigurationAckPolicy ackPolicy = ConsumerConfigurationAckPolicy.@explicit, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.CreateConsumerAsync(_name, consumer, ackPolicy, cancellationToken);
    }

    /// <summary>
    /// Creates new consumer for this stream if it doesn't exists or returns an existing one with the same name.
    /// </summary>
    /// <param name="request">Consumer creation request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">Ack policy is set to <c>none</c> or there is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<NatsJSConsumer> CreateConsumerAsync(ConsumerCreateRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.CreateConsumerAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets consumer information from the server and creates a NATS JetStream consumer <see cref="NatsJSConsumer"/>.
    /// </summary>
    /// <param name="consumer">Consumer name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream consumer object which can be used retrieving data from the stream.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<NatsJSConsumer> GetConsumerAsync(string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.GetConsumerAsync(_name, consumer, cancellationToken);
    }

    /// <summary>
    /// Enumerates through consumers belonging to this stream.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of consumer objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>
    /// Note that paging isn't implemented. You might receive only a partial list of consumers if there are a lot of them.
    /// </remarks>
    public IAsyncEnumerable<NatsJSConsumer> ListConsumersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.ListConsumersAsync(_name, cancellationToken);
    }

    /// <summary>
    /// Delete a consumer from this stream.
    /// </summary>
    /// <param name="consumer">Consumer name to be deleted.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether the deletion was successful.</returns>
    /// <exception cref="NatsJSException">There is an error retrieving the response or this consumer object isn't valid anymore because it was deleted earlier.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<bool> DeleteConsumerAsync(string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.DeleteConsumerAsync(_name, consumer, cancellationToken);
    }

    /// <summary>
    /// Retrieve the stream info from the server and update this stream.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
        Info = await _context.JSRequestResponseAsync<object, StreamInfoResponse>(
            subject: $"{_context.Opts.Prefix}.STREAM.INFO.{_name}",
            request: null,
            cancellationToken).ConfigureAwait(false);

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Stream '{_name}' is deleted");
    }
}
