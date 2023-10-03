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
            await nats.PublishAsync(
                "foo",
                0,
                opts: new NatsPubOpts { Serializer = new TestSerializer(), WaitUntilSent = false, SerializeEarly = true });
        });

        await Assert.ThrowsAsync<TestSerializerException>(async () =>
        {
            await nats.PublishAsync(
                "foo",
                0,
                opts: new NatsPubOpts { Serializer = new TestSerializer(), WaitUntilSent = true });
        });
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
