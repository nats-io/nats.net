using System.Buffers;
using System.Text;

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
    public void Utf8_serializer()
    {
        SerializeDeserialize<string>("foo", "foo");
        SerializeDeserialize<DateTime>(DateTime.MinValue, "01/01/0001 00:00:00");
        SerializeDeserialize<DateTimeOffset>(DateTimeOffset.MinValue, "01/01/0001 00:00:00 +00:00");
        SerializeDeserialize<Guid>(Guid.Empty, "00000000-0000-0000-0000-000000000000");
        SerializeDeserialize<TimeSpan>(TimeSpan.Zero, "00:00:00");
        SerializeDeserialize<bool>(true, "True");
        SerializeDeserialize<byte>(42, "42");
        SerializeDeserialize<decimal>(42.42m, "42.42");
        SerializeDeserialize<double>(42.42d, "42.42");
        SerializeDeserialize<float>(42.42f, "42.42");
        SerializeDeserialize<int>(42, "42");
        SerializeDeserialize<long>(42L, "42");
        SerializeDeserialize<sbyte>(42, "42");
        SerializeDeserialize<short>(42, "42");
        SerializeDeserialize<uint>(42, "42");
        SerializeDeserialize<ulong>(42, "42");
        SerializeDeserialize<ulong>(42, "42");

        // Test chaining
        Assert.Throws<TestSerializerException>(() => Serialize<TestData>(new TestData("42"), "throws exception"));
        Assert.Throws<TestSerializerException>(() => Deserialize<TestData>("throws exception", new TestData("42")));

        return;

        void SerializeDeserialize<T>(T actual, string expected)
        {
            Serialize(actual, expected);
            Deserialize(expected, actual);
        }

        void Serialize<T>(T value, string expected)
        {
            var buffer = new NatsBufferWriter<byte>();
            var serializer = new NatsUtf8PrimitivesSerializer<T>(new TestSerializer<T>());
            serializer.Serialize(buffer, value);
            var actual = Encoding.UTF8.GetString(buffer.WrittenMemory.Span);
            Assert.Equal(expected, actual);
        }

        void Deserialize<T>(string input, T expected)
        {
            var buffer = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(input));
            var serializer = new NatsUtf8PrimitivesSerializer<T>(new TestSerializer<T>());
            var actual = serializer.Deserialize(buffer);
            Assert.Equal(expected, actual);
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

        var serializer = new TestSerializerWithEmpty<TestData>();
        var sub = await nats.SubscribeCoreAsync("foo", serializer: serializer, cancellationToken: cancellationToken);

        await nats.PublishAsync("foo", cancellationToken: cancellationToken);
        await nats.PublishAsync("foo", "something", cancellationToken: cancellationToken);

        var result1 = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.NotNull(result1.Data);
        Assert.Equal("__EMPTY__", result1.Data.Name);

        var result2 = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.NotNull(result2.Data);
        Assert.Equal("something", result2.Data.Name);
    }

    [Fact]
    public async Task Deserialize_chained_with_empty()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection(new NatsOpts
        {
            SerializerRegistry = new TestSerializerRegistry(),
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await nats.ConnectAsync();

        var serializer = new TestSerializerWithEmpty<string>();
        var sub = await nats.SubscribeCoreAsync<TestData>("foo", cancellationToken: cancellationToken);

        await nats.PublishAsync("foo", cancellationToken: cancellationToken);
        await nats.PublishAsync("foo", "something", cancellationToken: cancellationToken);

        var result1 = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.NotNull(result1.Data);
        Assert.Equal("__EMPTY__", result1.Data.Name);

        var result2 = await sub.Msgs.ReadAsync(cancellationToken);
        Assert.NotNull(result2.Data);
        Assert.Equal("something", result2.Data.Name);
    }
}

public class TestSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerialize<T> GetSerializer<T>() => new NatsUtf8PrimitivesSerializer<T>(new TestSerializerWithEmpty<T>());

    public INatsDeserialize<T> GetDeserializer<T>() => new NatsUtf8PrimitivesSerializer<T>(new TestSerializerWithEmpty<T>());
}

public class TestSerializer<T> : INatsSerializer<T>
{
    public void Serialize(IBufferWriter<byte> bufferWriter, T? value) => throw new TestSerializerException();

    public T? Deserialize(in ReadOnlySequence<byte> buffer) => throw new TestSerializerException();
}

public class TestSerializerException : Exception
{
}

public class TestSerializerWithEmpty<T> : INatsSerializer<T>
{
    public T? Deserialize(in ReadOnlySequence<byte> buffer) => (T)(object)(buffer.IsEmpty
        ? new TestData("__EMPTY__")
        : new TestData(Encoding.ASCII.GetString(buffer)));

    public void Serialize(IBufferWriter<byte> bufferWriter, T value) => throw new Exception("not used");
}

public record TestData(string Name);
