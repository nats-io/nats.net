using System.Text;

// ReSharper disable AccessToDisposedClosure
namespace NATS.Client.CoreUnit.Tests;

public class ProtocolWriterTests
{
    private readonly ProtocolWriter _protocolWriter = new(Encoding.UTF8);
    private readonly ProtocolWriter _protocolWriterNoValidation = new(Encoding.UTF8, skipSubjectValidation: true);

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

    // Whitespace validation for subjects
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    [InlineData(" foo")]
    [InlineData("\tfoo")]
    [InlineData("\rfoo")]
    [InlineData("\nfoo")]
    public void WritePublish_SubjectWithWhitespace_Throws(string subject)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, subject, null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
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
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    // Empty subject validation
    [Fact]
    public void WritePublish_EmptySubject_Throws()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WritePublish(writer, string.Empty, null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    // Subscribe tests
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

    // Queue group can have dots
    [Theory]
    [InlineData("queue.group")]
    [InlineData(".queue")]
    [InlineData("queue.")]
    [InlineData("queue..group")]
    public void WriteSubscribe_QueueGroupWithDots_DoesNotThrow(string queueGroup)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, "foo.bar", queueGroup, null);
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
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
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
        action.Should().Throw<NatsException>().WithMessage("Queue group is invalid.");
    }

    [Fact]
    public void WriteSubscribe_EmptySubject_Throws()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, string.Empty, null, null);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public void WriteSubscribe_EmptyQueueGroup_Throws()
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriter.WriteSubscribe(writer, 1, "foo.bar", string.Empty, null);
        action.Should().Throw<NatsException>().WithMessage("Queue group is invalid.");
    }

    // SkipSubjectValidation tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public void WritePublish_WithSkipValidation_DoesNotThrow(string subject)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriterNoValidation.WritePublish(writer, subject, null, null, ReadOnlyMemory<byte>.Empty);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public void WriteSubscribe_WithSkipValidation_DoesNotThrow(string subject)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriterNoValidation.WriteSubscribe(writer, 1, subject, null, null);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("queue group")]
    [InlineData("queue\tgroup")]
    public void WriteSubscribe_QueueGroupWithSkipValidation_DoesNotThrow(string queueGroup)
    {
        using var writer = new NatsBufferWriter<byte>();
        var action = () => _protocolWriterNoValidation.WriteSubscribe(writer, 1, "foo.bar", queueGroup, null);
        action.Should().NotThrow();
    }
}
