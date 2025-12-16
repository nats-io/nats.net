using System.Text;
using NATS.Client.Core.Internal;

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
}

/// <summary>
/// Tests for subject validation at the API level.
/// Validation is performed in SubjectValidator and called from NatsConnection.PublishAsync/SubscribeAsync.
/// </summary>
public class SubjectValidatorTests
{
    // Wildcard subjects should be valid
    [Theory]
    [InlineData("foo.*")]
    [InlineData("foo.>")]
    [InlineData("*")]
    [InlineData(">")]
    [InlineData("foo.*.bar")]
    [InlineData("foo.bar.>")]
    public void ValidateSubject_WildcardSubject_DoesNotThrow(string subject)
    {
        var action = () => SubjectValidator.ValidateSubject(subject);
        action.Should().NotThrow();
    }

    // Whitespace validation for subjects (short path < 16 chars)
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    [InlineData(" foo")]
    [InlineData("\tfoo")]
    [InlineData("\rfoo")]
    [InlineData("\nfoo")]
    public void ValidateSubject_ShortSubjectWithWhitespace_Throws(string subject)
    {
        var action = () => SubjectValidator.ValidateSubject(subject);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    // Whitespace validation for subjects (long path >= 16 chars, SIMD)
    [Theory]
    [InlineData("foo.bar.baz.qux witespace")]
    [InlineData("foo.bar.baz.qux\twithtab")]
    [InlineData("foo.bar.baz.qux\rwithcr")]
    [InlineData("foo.bar.baz.qux\nwithlf")]
    [InlineData(" foo.bar.baz.qux.start")]
    public void ValidateSubject_LongSubjectWithWhitespace_Throws(string subject)
    {
        var action = () => SubjectValidator.ValidateSubject(subject);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    [Theory]
    [InlineData("reply to")]
    [InlineData("reply\tto")]
    [InlineData("reply\rto")]
    [InlineData("reply\nto")]
    public void ValidateReplyTo_WithWhitespace_Throws(string replyTo)
    {
        var action = () => SubjectValidator.ValidateReplyTo(replyTo);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    // Empty subject validation
    [Fact]
    public void ValidateSubject_EmptySubject_Throws()
    {
        var action = () => SubjectValidator.ValidateSubject(string.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public void ValidateSubject_NullSubject_Throws()
    {
        var action = () => SubjectValidator.ValidateSubject(null);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public void ValidateReplyTo_EmptyReplyTo_Throws()
    {
        var action = () => SubjectValidator.ValidateReplyTo(string.Empty);
        action.Should().Throw<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public void ValidateReplyTo_NullReplyTo_DoesNotThrow()
    {
        var action = () => SubjectValidator.ValidateReplyTo(null);
        action.Should().NotThrow();
    }

    // Queue group can have dots
    [Theory]
    [InlineData("queue.group")]
    [InlineData(".queue")]
    [InlineData("queue.")]
    [InlineData("queue..group")]
    public void ValidateQueueGroup_WithDots_DoesNotThrow(string queueGroup)
    {
        var action = () => SubjectValidator.ValidateQueueGroup(queueGroup);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("queue group")]
    [InlineData("queue\tgroup")]
    [InlineData("queue\rgroup")]
    [InlineData("queue\ngroup")]
    public void ValidateQueueGroup_WithWhitespace_Throws(string queueGroup)
    {
        var action = () => SubjectValidator.ValidateQueueGroup(queueGroup);
        action.Should().Throw<NatsException>().WithMessage("Queue group is invalid.");
    }

    [Fact]
    public void ValidateQueueGroup_EmptyQueueGroup_Throws()
    {
        var action = () => SubjectValidator.ValidateQueueGroup(string.Empty);
        action.Should().Throw<NatsException>().WithMessage("Queue group is invalid.");
    }

    [Fact]
    public void ValidateQueueGroup_NullQueueGroup_DoesNotThrow()
    {
        var action = () => SubjectValidator.ValidateQueueGroup(null);
        action.Should().NotThrow();
    }
}

/// <summary>
/// Tests for subject validation at the NatsConnection API level.
/// These tests verify that validation happens immediately when calling PublishAsync/SubscribeAsync,
/// before any connection attempt is made.
/// Note: SkipSubjectValidation defaults to true, so tests must explicitly enable validation.
/// </summary>
public class NatsConnectionSubjectValidationTests
{
    private static readonly NatsOpts OptsWithValidation = new() { SkipSubjectValidation = false };

    // PublishAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public async Task PublishAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.PublishAsync(subject);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task PublishAsync_EmptySubject_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.PublishAsync(string.Empty);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Theory]
    [InlineData("reply to")]
    [InlineData("reply\tto")]
    public async Task PublishAsync_ReplyToWithWhitespace_ThrowsImmediately(string replyTo)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.PublishAsync("foo.bar", replyTo: replyTo);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task PublishAsync_EmptyReplyTo_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.PublishAsync("foo.bar", replyTo: string.Empty);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // PublishAsync<T> tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task PublishAsyncT_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.PublishAsync(subject, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // SubscribeAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public async Task SubscribeAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () =>
        {
            await foreach (var _ in nats.SubscribeAsync<string>(subject))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task SubscribeAsync_EmptySubject_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () =>
        {
            await foreach (var _ in nats.SubscribeAsync<string>(string.Empty))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Theory]
    [InlineData("queue group")]
    [InlineData("queue\tgroup")]
    public async Task SubscribeAsync_QueueGroupWithWhitespace_ThrowsImmediately(string queueGroup)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () =>
        {
            await foreach (var _ in nats.SubscribeAsync<string>("foo.bar", queueGroup: queueGroup))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Queue group is invalid.");
    }

    [Fact]
    public async Task SubscribeAsync_EmptyQueueGroup_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () =>
        {
            await foreach (var _ in nats.SubscribeAsync<string>("foo.bar", queueGroup: string.Empty))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Queue group is invalid.");
    }

    // SubscribeCoreAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task SubscribeCoreAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.SubscribeCoreAsync<string>(subject);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Theory]
    [InlineData("queue group")]
    [InlineData("queue\tgroup")]
    public async Task SubscribeCoreAsync_QueueGroupWithWhitespace_ThrowsImmediately(string queueGroup)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.SubscribeCoreAsync<string>("foo.bar", queueGroup: queueGroup);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Queue group is invalid.");
    }

    // SkipSubjectValidation option tests (default is true, so validation is skipped by default)
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task PublishAsync_WithSkipValidation_DoesNotThrowOnInvalidSubject(string subject)
    {
        // Default SkipSubjectValidation=true, validation is bypassed at the API level.
        // The call will fail later (e.g., connection timeout) but not due to validation.
        await using var nats = new NatsConnection();

        // This should not throw NatsException for invalid subject
        // It will throw something else (timeout, connection error) since we're not connected
        Func<Task> action = async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await nats.PublishAsync(subject, cancellationToken: cts.Token);
        };

        // Should NOT throw "Subject is invalid."
        var ex = await action.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().NotBe("Subject is invalid.");
    }

    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task SubscribeAsync_WithSkipValidation_DoesNotThrowOnInvalidSubject(string subject)
    {
        // Default SkipSubjectValidation=true
        await using var nats = new NatsConnection();

        // This should not throw NatsException for invalid subject
        Func<Task> action = async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await foreach (var _ in nats.SubscribeAsync<string>(subject, cancellationToken: cts.Token))
            {
                // Should not reach here
            }
        };

        // Should NOT throw "Subject is invalid." - will throw timeout or connection error instead
        var ex = await action.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().NotBe("Subject is invalid.");
    }

    // RequestAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public async Task RequestAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.RequestAsync<string, string>(subject, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task RequestAsync_EmptySubject_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.RequestAsync<string, string>(string.Empty, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // RequestManyAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task RequestManyAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () =>
        {
            await foreach (var msg in nats.RequestManyAsync<string, string>(subject, "data"))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // CreateRequestSubAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task CreateRequestSubAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(OptsWithValidation);
        var action = async () => await nats.CreateRequestSubAsync<string, string>(subject, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }
}
