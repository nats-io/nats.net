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

        await Assert.ThrowsAsync<TestSerializerException>(async () =>
        {
            var signal = new WaitSignal<Exception>();

            var opts = new NatsPubOpts
            {
                Serializer = new TestSerializer(),
                WaitUntilSent = false,
                ErrorHandler = e =>
                {
                    signal.Pulse(e);
                },
            };

            await nats.PublishAsync(
                "foo",
                0,
                opts: opts);

            throw await signal;
        });

        await Assert.ThrowsAsync<TestSerializerException>(async () =>
        {
            await nats.PublishAsync(
                "foo",
                0,
                opts: new NatsPubOpts { Serializer = new TestSerializer(), WaitUntilSent = true });
        });

        // Check that our connection isn't affected by the exceptions
        await using var sub = await nats.SubscribeAsync<int>("foo");

        var rtt = await nats.PingAsync();
        Assert.True(rtt > TimeSpan.Zero);

        await nats.PublishAsync("foo", 1);

        var result = (await sub.Msgs.ReadAsync()).Data;

        Assert.Equal(1, result);
    }
}

public class TestSerializer : INatsSerializer
{
    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value) => throw new TestSerializerException();

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer) => throw new TestSerializerException();

    public object? Deserialize(in ReadOnlySequence<byte> buffer, Type type) => throw new TestSerializerException();
}

public class TestSerializerException : Exception
{
}
