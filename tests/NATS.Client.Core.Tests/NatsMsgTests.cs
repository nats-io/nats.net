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
        var msg = NatsMsg.Create(subject, data, Encoding.UTF8, replyTo: replyTo, headers: headers);

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
        var msg = NatsMsg.Create(subject, data, headers: headers, replyTo: replyTo);

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
        var msg = NatsMsg.Create(subject, data, serializer, headers: headers, replyTo: replyTo);

        var bufferWriter = new NatsPooledBufferWriter<byte>(256);
        serializer.Serialize(bufferWriter, data);
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
        var msg = NatsMsg.Create(subject, data);

        // Assert
        msg.Size.Should().Be(-1);
    }

    private class TestData
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;
    }
}
