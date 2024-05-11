using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public static class Utils
{
    public static ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(this NatsJSContext context, string stream, string consumer, CancellationToken cancellationToken = default)
        => context.CreateOrUpdateConsumerAsync(stream, new ConsumerConfig(consumer), cancellationToken);

    public static ValueTask<INatsJSStream> CreateStreamAsync(this NatsJSContext context, string stream, string[] subjects, CancellationToken cancellationToken = default)
        => context.CreateStreamAsync(new StreamConfig { Name = stream, Subjects = subjects }, cancellationToken);
}
