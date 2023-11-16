using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public static class Utils
{
    public static ValueTask<INatsJSConsumer> CreateConsumerAsync(this NatsJSContext context, string stream, string consumer, CancellationToken cancellationToken = default)
        => context.CreateConsumerAsync(stream, new ConsumerConfiguration { Name = consumer, DurableName = consumer, AckPolicy = ConsumerConfigurationAckPolicy.@explicit }, cancellationToken);

    public static ValueTask<INatsJSStream> CreateStreamAsync(this NatsJSContext context, string stream, string[] subjects, CancellationToken cancellationToken = default)
        => context.CreateStreamAsync(new StreamConfiguration { Name = stream, Subjects = subjects }, cancellationToken);
}
