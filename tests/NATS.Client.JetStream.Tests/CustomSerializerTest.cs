using System.Buffers;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class CustomSerializerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public CustomSerializerTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task When_consuming_ack_should_be_serialized_normally_if_custom_serializer_used()
    {
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            SerializerRegistry = new Level42SerializerRegistry(),
            RequestTimeout = TimeSpan.FromSeconds(10),
        });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);

        await js.PublishAsync($"{prefix}s1.1", new byte[] { 0 }, cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1.2", new byte[] { 0 }, cancellationToken: cts.Token);

        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        // single ack
        {
            var next = await consumer.NextAsync<byte[]>(cancellationToken: cts.Token);
            if (next is { } msg)
            {
                Assert.Equal(new byte[] { 42 }, msg.Data);
                await msg.AckAsync(cancellationToken: cts.Token);
            }
            else
            {
                Assert.Fail("No message received");
            }
        }

        // double ack
        {
            var next = await consumer.NextAsync<byte[]>(cancellationToken: cts.Token);
            if (next is { } msg)
            {
                Assert.Equal(new byte[] { 42 }, msg.Data);
                await msg.AckAsync(new AckOpts { DoubleAck = true }, cancellationToken: cts.Token);
            }
            else
            {
                Assert.Fail("No message received");
            }
        }

        await consumer.RefreshAsync(cts.Token);

        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    private class Level42Serializer<T> : INatsSerializer<T>
    {
        public void Serialize(IBufferWriter<byte> bufferWriter, T value)
        {
            bufferWriter.Write(new byte[] { 42 });
            bufferWriter.Advance(1);
        }

        public T Deserialize(in ReadOnlySequence<byte> buffer) => (T)(object)new byte[] { 42 };

        public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => throw new NotImplementedException();
    }

    private class Level42SerializerRegistry : INatsSerializerRegistry
    {
        public INatsSerialize<T> GetSerializer<T>() => new Level42Serializer<T>();

        public INatsDeserialize<T> GetDeserializer<T>() => new Level42Serializer<T>();
    }
}
