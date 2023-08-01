using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSStream
{
    private readonly NatsJSContext _context;
    private readonly string _name;
    private bool _deleted;

    public NatsJSStream(NatsJSContext context, StreamInfo info)
    {
        _context = context;
        Info = info;
        _name = info.Config.Name;
    }

    public StreamInfo Info { get; private set; }

    public async ValueTask<bool> DeleteAsync(string stream, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _deleted = await _context.DeleteStreamAsync(_name, cancellationToken);
    }

    public async ValueTask UpdateAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        var response = await _context.UpdateStreamAsync(request, cancellationToken);
        Info = response.Info;
    }

    public async IAsyncEnumerable<NatsJSConsumer> ListConsumersAsync(string stream, ConsumerListRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        await foreach (var consumer in _context.ListConsumersAsync(_name, request, cancellationToken))
            yield return consumer;
    }

    public ValueTask<NatsJSConsumer> CreateConsumerAsync(ConsumerCreateRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.CreateConsumerAsync(request, cancellationToken);
    }

    public ValueTask<NatsJSConsumer> GetConsumerAsync(string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.GetConsumerAsync(_name, consumer, cancellationToken);
    }

    public ValueTask<bool> DeleteConsumerAsync(string consumer, CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();
        return _context.DeleteConsumerAsync(_name, consumer, cancellationToken);
    }

    private void ThrowIfDeleted()
    {
        if (_deleted)
            throw new NatsJSException($"Stream '{_name}' is deleted");
    }
}
