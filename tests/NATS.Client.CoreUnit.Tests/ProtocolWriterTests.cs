using System.Text;

// ReSharper disable AccessToDisposedClosure
namespace NATS.Client.CoreUnit.Tests;

public class ProtocolWriterTests
{
    private readonly ProtocolWriter _protocolWriter = new(Encoding.UTF8);

    [Fact]
    public void WritePublish_ValidSubject_DoesNotThrow()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, "foo.bar", null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().NotThrow();
    }

    [Fact]
    public void WritePublish_ValidSubjectWithReplyTo_DoesNotThrow()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, "foo.bar", "reply.to", null, ReadOnlyMemory<byte>.Empty);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public void WritePublish_SubjectWithWhitespace_Throws(string subject)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, subject, null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }

    [Theory]
    [InlineData("reply to")]
    [InlineData("reply\tto")]
    [InlineData("reply\rto")]
    [InlineData("reply\nto")]
    public void WritePublish_ReplyToWithWhitespace_Throws(string replyTo)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, "foo.bar", replyTo, null, ReadOnlyMemory<byte>.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }

    [Fact]
    public void WriteSubscribe_ValidSubject_DoesNotThrow()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, "foo.bar", null, null);
        action.Should().NotThrow();
    }

    [Fact]
    public void WriteSubscribe_ValidSubjectWithQueueGroup_DoesNotThrow()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, "foo.bar", "queue-group", null);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public void WriteSubscribe_SubjectWithWhitespace_Throws(string subject)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, subject, null, null);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }

    [Theory]
    [InlineData("queue group")]
    [InlineData("queue\tgroup")]
    [InlineData("queue\rgroup")]
    [InlineData("queue\ngroup")]
    public void WriteSubscribe_QueueGroupWithWhitespace_Throws(string queueGroup)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, "foo.bar", queueGroup, null);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }

    // Test for whitespace at the START of strings (index 0)
    [Theory]
    [InlineData(" foo")]
    [InlineData("\tfoo")]
    [InlineData("\rfoo")]
    [InlineData("\nfoo")]
    public void WritePublish_SubjectWithLeadingWhitespace_Throws(string subject)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, subject, null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }

    // Test for empty strings
    [Fact]
    public void WritePublish_EmptySubject_Throws()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, string.Empty, null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }

    [Fact]
    public void WriteSubscribe_EmptySubject_Throws()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, string.Empty, null, null);
        action.Should().Throw<NatsException>().WithMessage("Subject cannot be empty or contain whitespace.");
    }
}
