using System.Text;
using NATS.Client.Core.Internal;

// ReSharper disable AccessToDisposedClosure
namespace NATS.Client.CoreUnit.Tests;

public class NatsConnectionSubjectValidationTests
{
    // PublishAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public async Task PublishAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.PublishAsync(subject);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task PublishAsync_EmptySubject_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.PublishAsync(string.Empty);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Theory]
    [InlineData("reply to")]
    [InlineData("reply\tto")]
    public async Task PublishAsync_ReplyToWithWhitespace_ThrowsImmediately(string replyTo)
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.PublishAsync("foo.bar", replyTo: replyTo);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task PublishAsync_EmptyReplyTo_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.PublishAsync("foo.bar", replyTo: string.Empty);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // PublishAsync<T> tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task PublishAsyncT_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
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
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () =>
        {
            await foreach (var unused in nats.SubscribeAsync<string>(subject))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task SubscribeAsync_EmptySubject_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () =>
        {
            await foreach (var unused in nats.SubscribeAsync<string>(string.Empty))
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
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () =>
        {
            await foreach (var unused in nats.SubscribeAsync<string>("foo.bar", queueGroup: queueGroup))
            {
                // Should not reach here
            }
        };
        await action.Should().ThrowAsync<NatsException>().WithMessage("Queue group is invalid.");
    }

    [Fact]
    public async Task SubscribeAsync_EmptyQueueGroup_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () =>
        {
            await foreach (var unused in nats.SubscribeAsync<string>("foo.bar", queueGroup: string.Empty))
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
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.SubscribeCoreAsync<string>(subject);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Theory]
    [InlineData("queue group")]
    [InlineData("queue\tgroup")]
    public async Task SubscribeCoreAsync_QueueGroupWithWhitespace_ThrowsImmediately(string queueGroup)
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.SubscribeCoreAsync<string>("foo.bar", queueGroup: queueGroup);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Queue group is invalid.");
    }

    // Obsolete SkipSubjectValidation must no longer disable validation
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task PublishAsync_WithObsoleteSkipValidation_StillThrows(string subject)
    {
#pragma warning disable CS0618 // SkipSubjectValidation is obsolete and ignored
        await using var nats = new NatsConnection(NatsOpts.Default with { SkipSubjectValidation = true });
#pragma warning restore CS0618
        var action = async () => await nats.PublishAsync(subject);
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // RequestAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\nbar")]
    public async Task RequestAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.RequestAsync<string, string>(subject, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    [Fact]
    public async Task RequestAsync_EmptySubject_ThrowsImmediately()
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.RequestAsync<string, string>(string.Empty, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // RequestManyAsync tests
    [Theory]
    [InlineData("foo bar")]
    [InlineData("foo\tbar")]
    public async Task RequestManyAsync_SubjectWithWhitespace_ThrowsImmediately(string subject)
    {
        await using var nats = new NatsConnection(NatsOpts.Default);
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
        await using var nats = new NatsConnection(NatsOpts.Default);
        var action = async () => await nats.CreateRequestSubAsync<string, string>(subject, "data");
        await action.Should().ThrowAsync<NatsException>().WithMessage("Subject is invalid.");
    }

    // Tests that verify exceptions are thrown SYNCHRONOUSLY (before any await/iteration)
    // This is critical for good user experience - users should get immediate feedback
    [Fact]
    public void SubscribeAsync_ThrowsSynchronously_BeforeIteration()
    {
        var nats = new NatsConnection(NatsOpts.Default);

        // Exception should be thrown HERE, when calling SubscribeAsync,
        // NOT when iterating the result
        var exception = Assert.Throws<NatsException>(() =>
        {
            _ = nats.SubscribeAsync<string>("foo bar"); // No await, no iteration
        });

        Assert.Equal("Subject is invalid.", exception.Message);
    }

    [Fact]
    public void SubscribeAsync_QueueGroup_ThrowsSynchronously_BeforeIteration()
    {
        var nats = new NatsConnection(NatsOpts.Default);

        var exception = Assert.Throws<NatsException>(() =>
        {
            _ = nats.SubscribeAsync<string>("valid.subject", queueGroup: "queue group");
        });

        Assert.Equal("Queue group is invalid.", exception.Message);
    }

    [Fact]
    public void SubscribeCoreAsync_ThrowsSynchronously_BeforeAwait()
    {
        var nats = new NatsConnection(NatsOpts.Default);

        // Exception should be thrown HERE, when calling SubscribeCoreAsync,
        // NOT when awaiting the result
        var exception = Assert.Throws<NatsException>(() =>
        {
            _ = nats.SubscribeCoreAsync<string>("foo bar"); // No await
        });

        Assert.Equal("Subject is invalid.", exception.Message);
    }

    [Fact]
    public void RequestManyAsync_ThrowsSynchronously_BeforeIteration()
    {
        var nats = new NatsConnection(NatsOpts.Default);

        // Exception should be thrown HERE, when calling RequestManyAsync,
        // NOT when iterating the result
        var exception = Assert.Throws<NatsException>(() =>
        {
            _ = nats.RequestManyAsync<string, string>("foo bar", "data");
        });

        Assert.Equal("Subject is invalid.", exception.Message);
    }
}
