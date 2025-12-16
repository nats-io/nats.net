using System.Text;
using NATS.Client.Core.Internal;

// ReSharper disable AccessToDisposedClosure
namespace NATS.Client.CoreUnit.Tests;

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
