using System.Buffers;

namespace NATS.Client.Core.Tests;

public class SerializerTest
{
    private readonly ITestOutputHelper _output;

    public SerializerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Serializer_exceptions()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        await Assert.ThrowsAsync<TestSerializerException>(() =>
            nats.PublishAsync(
                "foo",
                0,
                serializer: new TestSerializer<int>()).AsTask());

        // Check that our connection isn't affected by the exceptions
        await using var sub = await nats.SubscribeCoreAsync<int>("foo");

        var rtt = await nats.PingAsync();
        Assert.True(rtt > TimeSpan.Zero);

        await nats.PublishAsync("foo", 1);

        var result = (await sub.Msgs.ReadAsync()).Data;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task NatsMemoryOwner_empty_payload_should_not_throw()
    {
        await using var server = NatsServer.Start();
        var nats = server.CreateClientConnection();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await nats.ConnectAsync();

        var sub = await nats.SubscribeCoreAsync<NatsMemoryOwner<byte>>("foo", cancellationToken: cancellationToken);
        await nats.PingAsync(cancellationToken);
        await nats.PublishAsync("foo", cancellationToken: cancellationToken);

        var msg = await sub.Msgs.ReadAsync(cancellationToken);

        Assert.Equal(0, msg.Data.Length);

        using (msg.Data)
        {
            Assert.Equal(0, msg.Data.Memory.Length);
            Assert.Equal(0, msg.Data.Span.Length);
        }
    }

    [Fact]
    public async Task Deserialize_with_empty()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await nats.ConnectAsync();

        var serializer = new TestDeserializeWithEmpty<int>();
        var sub = await nats.SubscribeCoreAsync("foo", serializer: serializer, cancellationToken: cancellationToken);

        await nats.PublishAsync("foo", cancellationToken: cancellationToken);

        var result = await sub.Msgs.ReadAsync(cancellationToken);

        Assert.Equal(42, result.Data);
    }
}

public class TestSerializer<T> : INatsSerialize<T>, INatsDeserialize<T>
{
    public void Serialize(IBufferWriter<byte> bufferWriter, T? value) => throw new TestSerializerException();

    public T? Deserialize(in ReadOnlySequence<byte> buffer) => throw new TestSerializerException();
}

public class TestSerializerException : Exception
{
}

public class TestDeserializeWithEmpty<T> : INatsDeserialize<T>
{
    public T? Deserialize(in ReadOnlySequence<byte> buffer) => (T)(object)42;
}
