using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PushFlowControlTest(NatsServerFixture server)
{
    [Fact]
    public async Task Push_flow_control_burst()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                FlowControl = true,
                IdleHeartbeat = TimeSpan.FromSeconds(5),
            },
            cts.Token);

        // Publish 500 messages with 512-byte payload to trigger flow control
        var payload = new byte[512];
        var msgTask = Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
            {
                payload[0] = (byte)i;
                payload[1] = (byte)(i >> 8);
                var ack = await js.PublishAsync($"{prefix}s1.foo", payload, cancellationToken: cts.Token);
                ack.EnsureSuccess();
            }
        });

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: cts.Token))
        {
            var data = msg.Data!;
            Assert.Equal((byte)count, data[0]);
            Assert.Equal((byte)(count >> 8), data[1]);
            await msg.AckAsync(cancellationToken: cts.Token);
            count++;
            if (count == 500)
                break;
        }

        await msgTask;
        Assert.Equal(500, count);
    }
}
