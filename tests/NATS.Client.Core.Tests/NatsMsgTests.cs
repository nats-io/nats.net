using System.Buffers;
using System.Text;
using NATS.Client.Serializers.Json;

namespace NATS.Client.Core.Tests;

public class NatsMsgTests
{
    [Fact]
    public void Create_nats_msg_by_string()
    {
        // Arrange
        const string subject = "test";
        const string data = "12345";
        const string replyTo = "reply";
        var headers = new NatsHeaders();

        // Act
        var builder = new NatsMsgBuilder<string>
        {
            Subject = subject,
            Data = data,
            ReplyTo = replyTo,
            Headers = headers,
        };
        var msg = builder.Msg;

        // Assert
        var expectedSize = subject.Length + replyTo.Length + headers.GetBytesLength() + Encoding.UTF8.GetByteCount(data);
        msg.Size.Should().Be(expectedSize);
    }

    [Fact]
    public void Create_nats_msg_by_byte_array()
    {
        // Arrange
        const string subject = "test";
        const string replyTo = "reply";
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var headers = new NatsHeaders();

        // Act
        var builder = new NatsMsgBuilder<byte[]>
        {
            Subject = subject,
            Data = data,
            ReplyTo = replyTo,
            Headers = headers,
        };
        var msg = builder.Msg;

        // Assert
        var expectedSize = subject.Length + (replyTo?.Length ?? 0) + headers.GetBytesLength() + data.Length;
        msg.Size.Should().Be(expectedSize);
    }

    [Fact]
    public void Create_nats_msg_by_object()
    {
        // Arrange
        const string subject = "test";
        const string replyTo = "reply";
        var data = new TestData { Id = 1, Name = "example" };
        var headers = new NatsHeaders();

        var serializer = new NatsJsonSerializer<TestData>();

        // Act
        var builder = new NatsMsgBuilder<TestData>
        {
            Subject = subject,
            Data = data,
            Serializer = serializer,
            Headers = headers,
            ReplyTo = replyTo,
        };
        var msg = builder.Msg;

        var bufferWriter = new NatsPooledBufferWriter<byte>(256);
        serializer.Serialize(bufferWriter, data);
        var serializedSize = bufferWriter.WrittenCount;

        var expectedSize = subject.Length + (replyTo?.Length ?? 0) + headers.GetBytesLength() + serializedSize;

        // Assert
        msg.Size.Should().Be(expectedSize);
    }

    [Fact]
    public void Create_nats_msg_by_object_with_header_aware_serializer()
    {
        // Arrange
        const string subject = "test";
        const string replyTo = "reply";
        var data = new TestData { Id = 1, Name = "example" };
        var headers = new NatsHeaders { { "X-Type", "test-data" } };

        var serializer = new HeaderAwareSerializer<TestData>();

        // Act
        var builder = new NatsMsgBuilder<TestData>
        {
            Subject = subject,
            Data = data,
            Serializer = serializer,
            Headers = headers,
            ReplyTo = replyTo,
        };
        var msg = builder.Msg;

        var bufferWriter = new NatsPooledBufferWriter<byte>(256);
        ((INatsSerializeWithContext<TestData>)serializer).Serialize(bufferWriter, data, new NatsMsgContext(subject, replyTo, headers));
        var serializedSize = bufferWriter.WrittenCount;

        var expectedSize = subject.Length + (replyTo?.Length ?? 0) + headers.GetBytesLength() + serializedSize;

        // Assert
        msg.Size.Should().Be(expectedSize);
    }

    [Fact]
    public void Create_nats_msg_by_object_without_serializer()
    {
        // Arrange
        const string subject = "test";
        var data = new TestData { Id = 1, Name = "example" };

        // Act
        var builder = new NatsMsgBuilder<TestData>
        {
            Subject = subject,
            Data = data,
        };
        var msg = builder.Msg;

        // Assert
        msg.Size.Should().Be(0);
    }

    private class TestData
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;
    }

    private class HeaderAwareSerializer<T> : INatsSerializer<T>, INatsSerializerWithContext<T>
    {
        private readonly NatsJsonSerializer<T> _inner = new();

        public void Serialize(IBufferWriter<byte> bufferWriter, T value) => _inner.Serialize(bufferWriter, value);

        public T? Deserialize(in ReadOnlySequence<byte> buffer) => _inner.Deserialize(buffer);

        public void Serialize(IBufferWriter<byte> bufferWriter, T value, in NatsMsgContext context)
        {
            // Write a header-based prefix before the JSON payload
            if (context.Headers != null && context.Headers.TryGetValue("X-Type", out var values))
            {
                var prefix = Encoding.UTF8.GetBytes(values.ToString() + ":");
                var span = bufferWriter.GetSpan(prefix.Length);
                prefix.CopyTo(span);
                bufferWriter.Advance(prefix.Length);
            }

            _inner.Serialize(bufferWriter, value);
        }

        public T? Deserialize(in ReadOnlySequence<byte> buffer, in NatsMsgContext context) =>
            _inner.Deserialize(buffer);

        public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => this;
    }
}
