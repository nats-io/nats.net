using System.Buffers;
using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class CustomSerializerTest
{
    [Fact]
    public async Task When_consuming_ack_should_be_serialized_normally_if_custom_serializer_used()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection(new NatsOpts
        {
            SerializerRegistry = new Level42SerializerRegistry(),
            RequestTimeout = TimeSpan.FromSeconds(10),
        });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        await js.PublishAsync("s1.1", new byte[] { 0 }, cancellationToken: cts.Token);
        await js.PublishAsync("s1.2", new byte[] { 0 }, cancellationToken: cts.Token);

        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

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
