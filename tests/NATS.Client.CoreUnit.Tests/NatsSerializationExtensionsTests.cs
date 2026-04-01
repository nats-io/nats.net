using System.Buffers;

namespace NATS.Client.CoreUnit.Tests;

public class NatsSerializationExtensionsTests
{
    [Fact]
    public void Serialize_with_context_aware_serializer_calls_context_overload()
    {
        var serializer = new TrackingSerializerWithContext();
        var buffer = new NatsPooledBufferWriter<byte>(256);
        var context = new NatsMsgContext { Subject = "test", Headers = new NatsHeaders { { "X-Test", "value" } } };

        ((INatsSerialize<string>)serializer).Serialize(buffer, "test", in context);

        serializer.ContextSerializeCalled.Should().BeTrue();
        serializer.StandardSerializeCalled.Should().BeFalse();
    }

    [Fact]
    public void Serialize_without_context_aware_serializer_falls_back()
    {
        var serializer = new TrackingSerializer();
        var buffer = new NatsPooledBufferWriter<byte>(256);
        var context = new NatsMsgContext { Subject = "test", Headers = new NatsHeaders { { "X-Test", "value" } } };

        ((INatsSerialize<string>)serializer).Serialize(buffer, "test", in context);

        serializer.StandardSerializeCalled.Should().BeTrue();
    }

    [Fact]
    public void Serialize_with_null_headers_still_calls_context_overload()
    {
        var serializer = new TrackingSerializerWithContext();
        var buffer = new NatsPooledBufferWriter<byte>(256);
        var context = new NatsMsgContext { Subject = "test" };

        ((INatsSerialize<string>)serializer).Serialize(buffer, "test", in context);

        serializer.ContextSerializeCalled.Should().BeTrue();
        serializer.StandardSerializeCalled.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_with_context_aware_deserializer_calls_context_overload()
    {
        var deserializer = new TrackingDeserializerWithContext();
        var buffer = new ReadOnlySequence<byte>(new byte[] { 1 });
        var context = new NatsMsgContext { Subject = "test", Headers = new NatsHeaders { { "X-Test", "value" } } };

        ((INatsDeserialize<string>)deserializer).Deserialize(buffer, in context);

        deserializer.ContextDeserializeCalled.Should().BeTrue();
        deserializer.StandardDeserializeCalled.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_without_context_aware_deserializer_falls_back()
    {
        var deserializer = new TrackingDeserializer();
        var buffer = new ReadOnlySequence<byte>(new byte[] { 1 });
        var context = new NatsMsgContext { Subject = "test", Headers = new NatsHeaders { { "X-Test", "value" } } };

        ((INatsDeserialize<string>)deserializer).Deserialize(buffer, in context);

        deserializer.StandardDeserializeCalled.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_with_null_headers_still_calls_context_overload()
    {
        var deserializer = new TrackingDeserializerWithContext();
        var buffer = new ReadOnlySequence<byte>(new byte[] { 1 });
        var context = new NatsMsgContext { Subject = "test" };

        ((INatsDeserialize<string>)deserializer).Deserialize(buffer, in context);

        deserializer.ContextDeserializeCalled.Should().BeTrue();
        deserializer.StandardDeserializeCalled.Should().BeFalse();
    }

    private class TrackingSerializer : INatsSerialize<string>
    {
        public bool StandardSerializeCalled { get; private set; }

        public void Serialize(IBufferWriter<byte> bufferWriter, string value) =>
            StandardSerializeCalled = true;
    }

    private class TrackingSerializerWithContext : INatsSerialize<string>, INatsSerializeWithContext<string>
    {
        public bool StandardSerializeCalled { get; private set; }

        public bool ContextSerializeCalled { get; private set; }

        public void Serialize(IBufferWriter<byte> bufferWriter, string value) =>
            StandardSerializeCalled = true;

        public void Serialize(IBufferWriter<byte> bufferWriter, string value, in NatsMsgContext context) =>
            ContextSerializeCalled = true;
    }

    private class TrackingDeserializer : INatsDeserialize<string>
    {
        public bool StandardDeserializeCalled { get; private set; }

        public string? Deserialize(in ReadOnlySequence<byte> buffer)
        {
            StandardDeserializeCalled = true;
            return null;
        }
    }

    private class TrackingDeserializerWithContext : INatsDeserialize<string>, INatsDeserializeWithContext<string>
    {
        public bool StandardDeserializeCalled { get; private set; }

        public bool ContextDeserializeCalled { get; private set; }

        public string? Deserialize(in ReadOnlySequence<byte> buffer)
        {
            StandardDeserializeCalled = true;
            return null;
        }

        public string? Deserialize(in ReadOnlySequence<byte> buffer, in NatsMsgContext context)
        {
            ContextDeserializeCalled = true;
            return null;
        }
    }
}
