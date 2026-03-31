using System.Buffers;

namespace NATS.Client.CoreUnit.Tests;

public class NatsSerializationExtensionsTests
{
    [Fact]
    public void Serialize_with_header_aware_serializer_calls_header_overload()
    {
        var serializer = new TrackingSerializerWithHeaders();
        var buffer = new NatsPooledBufferWriter<byte>(256);
        var headers = new NatsHeaders { { "X-Test", "value" } };

        ((INatsSerialize<string>)serializer).Serialize(buffer, "test", headers);

        serializer.HeaderSerializeCalled.Should().BeTrue();
        serializer.StandardSerializeCalled.Should().BeFalse();
    }

    [Fact]
    public void Serialize_without_header_aware_serializer_falls_back()
    {
        var serializer = new TrackingSerializer();
        var buffer = new NatsPooledBufferWriter<byte>(256);
        var headers = new NatsHeaders { { "X-Test", "value" } };

        ((INatsSerialize<string>)serializer).Serialize(buffer, "test", headers);

        serializer.StandardSerializeCalled.Should().BeTrue();
    }

    [Fact]
    public void Serialize_with_null_headers_skips_header_overload()
    {
        var serializer = new TrackingSerializerWithHeaders();
        var buffer = new NatsPooledBufferWriter<byte>(256);

        ((INatsSerialize<string>)serializer).Serialize(buffer, "test", null);

        serializer.HeaderSerializeCalled.Should().BeFalse();
        serializer.StandardSerializeCalled.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_with_header_aware_deserializer_calls_header_overload()
    {
        var deserializer = new TrackingDeserializerWithHeaders();
        var buffer = new ReadOnlySequence<byte>(new byte[] { 1 });
        var headers = new NatsHeaders { { "X-Test", "value" } };

        ((INatsDeserialize<string>)deserializer).Deserialize(buffer, headers);

        deserializer.HeaderDeserializeCalled.Should().BeTrue();
        deserializer.StandardDeserializeCalled.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_without_header_aware_deserializer_falls_back()
    {
        var deserializer = new TrackingDeserializer();
        var buffer = new ReadOnlySequence<byte>(new byte[] { 1 });
        var headers = new NatsHeaders { { "X-Test", "value" } };

        ((INatsDeserialize<string>)deserializer).Deserialize(buffer, headers);

        deserializer.StandardDeserializeCalled.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_with_null_headers_still_calls_header_overload()
    {
        var deserializer = new TrackingDeserializerWithHeaders();
        var buffer = new ReadOnlySequence<byte>(new byte[] { 1 });

        ((INatsDeserialize<string>)deserializer).Deserialize(buffer, null);

        deserializer.HeaderDeserializeCalled.Should().BeTrue();
        deserializer.StandardDeserializeCalled.Should().BeFalse();
    }

    private class TrackingSerializer : INatsSerialize<string>
    {
        public bool StandardSerializeCalled { get; private set; }

        public void Serialize(IBufferWriter<byte> bufferWriter, string value) =>
            StandardSerializeCalled = true;
    }

    private class TrackingSerializerWithHeaders : INatsSerialize<string>, INatsSerializeWithHeaders<string>
    {
        public bool StandardSerializeCalled { get; private set; }

        public bool HeaderSerializeCalled { get; private set; }

        public void Serialize(IBufferWriter<byte> bufferWriter, string value) =>
            StandardSerializeCalled = true;

        public void Serialize(IBufferWriter<byte> bufferWriter, string value, INatsHeaders? headers) =>
            HeaderSerializeCalled = true;
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

    private class TrackingDeserializerWithHeaders : INatsDeserialize<string>, INatsDeserializeWithHeaders<string>
    {
        public bool StandardDeserializeCalled { get; private set; }

        public bool HeaderDeserializeCalled { get; private set; }

        public string? Deserialize(in ReadOnlySequence<byte> buffer)
        {
            StandardDeserializeCalled = true;
            return null;
        }

        public string? Deserialize(in ReadOnlySequence<byte> buffer, INatsHeaders? headers)
        {
            HeaderDeserializeCalled = true;
            return null;
        }
    }
}
