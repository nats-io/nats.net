using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public static class Utils
{
    public static ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(this NatsJSContext context, string stream, string consumer, CancellationToken cancellationToken = default)
        => context.CreateOrUpdateConsumerAsync(stream, new ConsumerConfig(consumer), cancellationToken);

    public static ValueTask<INatsJSStream> CreateStreamAsync(this NatsJSContext context, string stream, string[] subjects, CancellationToken cancellationToken = default)
        => context.CreateStreamAsync(new StreamConfig { Name = stream, Subjects = subjects }, cancellationToken);

    public static NatsProxy CreateProxy(this NatsServerFixture server)
        => new(new Uri(server.Url).Port);

    public static NatsConnection CreateNatsConnection(this NatsProxy proxy)
        => new(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{proxy.Port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });

    public static NatsConnection CreateNatsConnection(this NatsServerFixture server)
        => new(new NatsOpts
        {
            Url = server.Url,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });
}
