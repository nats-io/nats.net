using System.Buffers;
using System.Text;
using Microsoft.Extensions.Primitives;
using NATS.Client.Core;

namespace NATS.Client.Abstractions.Tests;

// Everything here is written against NATS.Client.Abstractions only (see the csproj: no Core
// reference). It stands in for a third-party adapter such as the CloudEvents SDK from issue #851,
// proving that consuming code can deconstruct a received message and construct an outgoing one
// using just the envelope (NatsMsgContext / INatsHeaders) and the serializer interfaces.
public class AbstractionsBoundaryTest
{
    [Fact]
    public void Deserialize_with_context_reads_envelope_and_payload()
    {
        var headers = new TestHeaders { ["ce-type"] = "com.example.order.created" };
        var context = new NatsMsgContext("orders.new", replyTo: "_INBOX.reply", headers: headers);
        var payload = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("order-123"));

        INatsDeserialize<CloudEventLike> deserializer = new CloudEventDeserializer();
        var result = deserializer.Deserialize(payload, context);

        result.Should().NotBeNull();
        result!.Source.Should().Be("orders.new");
        result.Type.Should().Be("com.example.order.created");
        result.Data.Should().Be("order-123");
    }

    [Fact]
    public void Serialize_writes_payload_through_the_interface()
    {
        INatsSerialize<CloudEventLike> serializer = new CloudEventDeserializer();
        var writer = new TestBufferWriter();

        serializer.Serialize(writer, new CloudEventLike("orders.new", "com.example.order.created", "order-123"));

        Encoding.UTF8.GetString(writer.ToArray()).Should().Be("order-123");
    }

    private sealed class CloudEventLike
    {
        public CloudEventLike(string source, string? type, string data)
        {
            Source = source;
            Type = type;
            Data = data;
        }

        public string Source { get; }

        public string? Type { get; }

        public string Data { get; }
    }

    // A context-aware (de)serializer backed only by Abstractions types.
    private sealed class CloudEventDeserializer : INatsSerializeWithContext<CloudEventLike>, INatsDeserializeWithContext<CloudEventLike>
    {
        public void Serialize(IBufferWriter<byte> bufferWriter, CloudEventLike value)
        {
            var bytes = Encoding.UTF8.GetBytes(value.Data);
            bufferWriter.Write(bytes);
        }

        public void Serialize(IBufferWriter<byte> bufferWriter, CloudEventLike value, in NatsMsgContext context) =>
            Serialize(bufferWriter, value);

        public CloudEventLike? Deserialize(in ReadOnlySequence<byte> buffer) =>
            new(source: string.Empty, type: null, data: Encoding.UTF8.GetString(buffer.ToArray()));

        public CloudEventLike? Deserialize(in ReadOnlySequence<byte> buffer, in NatsMsgContext context)
        {
            var type = context.Headers is not null && context.Headers.TryGetValue("ce-type", out var values)
                ? values.ToString()
                : null;

            return new CloudEventLike(
                source: context.Subject,
                type: type,
                data: Encoding.UTF8.GetString(buffer.ToArray()));
        }
    }

    // INatsHeaders adds nothing beyond IDictionary<string, StringValues>, so Dictionary supplies
    // every member. A consumer can read headers off a received message with only Abstractions.
    private sealed class TestHeaders : Dictionary<string, StringValues>, INatsHeaders;

    // Minimal IBufferWriter<byte>; ArrayBufferWriter<byte> is unavailable on net481.
    private sealed class TestBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[256];
        private int _written;

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public byte[] ToArray() => _buffer.AsSpan(0, _written).ToArray();

        private void Ensure(int sizeHint)
        {
            if (_written + Math.Max(sizeHint, 1) <= _buffer.Length)
                return;

            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _written + sizeHint));
        }
    }
}
