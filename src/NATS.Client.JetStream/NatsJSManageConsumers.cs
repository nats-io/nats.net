using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSManageConsumers
{
    private readonly NatsJSContext _context;

    public NatsJSManageConsumers(NatsJSContext context) => _context = context;

    public ValueTask<ConsumerInfo> CreateAsync(
        string stream,
        string consumer,
        ConsumerConfigurationAckPolicy ackPolicy = ConsumerConfigurationAckPolicy.@explicit,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            new ConsumerCreateRequest
            {
                StreamName = stream,
                Config = new ConsumerConfiguration
                {
                    Name = consumer,
                    DurableName = consumer,
                    AckPolicy = ackPolicy,
                },
            },
            cancellationToken);

    public ValueTask<ConsumerInfo> CreateAsync(
        ConsumerCreateRequest request,
        CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<ConsumerCreateRequest, ConsumerInfo>(
            subject: $"{_context.Options.Prefix}.CONSUMER.CREATE.{request.StreamName}.{request.Config.Name}",
            request,
            cancellationToken);

    public ValueTask<ConsumerInfo> GetAsync(string stream, string consumer, CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<object, ConsumerInfo>(
            subject: $"{_context.Options.Prefix}.CONSUMER.INFO.{stream}.{consumer}",
            request: null,
            cancellationToken);

    public ValueTask<ConsumerListResponse> ListAsync(string stream, ConsumerListRequest request, CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<ConsumerListRequest, ConsumerListResponse>(
            subject: $"{_context.Options.Prefix}.CONSUMER.LIST.{stream}",
            request,
            cancellationToken);

    public ValueTask<ConsumerDeleteResponse> DeleteAsync(string stream, string consumer, CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<object, ConsumerDeleteResponse>(
            subject: $"{_context.Options.Prefix}.CONSUMER.DELETE.{stream}.{consumer}",
            request: null,
            cancellationToken);
}
