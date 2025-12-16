namespace NATS.Client.CoreUnit.Tests;

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
