using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PublishTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public PublishTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Publish_test()
    {
        await using var nats = _server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.>" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        // Publish
        {
            var ack = await js.PublishAsync(
                $"{prefix}s1.foo",
                new TestData
                {
                    Test = 1,
                },
                serializer: TestDataJsonSerializer<TestData>.Default,
                cancellationToken: cts.Token);
            Assert.Null(ack.Error);
            Assert.Equal(1, (int)ack.Seq);
            Assert.Equal($"{prefix}s1", ack.Stream);
            Assert.False(ack.Duplicate);
            Assert.True(ack.IsSuccess());
        }

        // Duplicate
        {
            var ack1 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "2" },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);
            Assert.Equal(2, (int)ack1.Seq);
            Assert.False(ack1.Duplicate);
            Assert.True(ack1.IsSuccess());

            var ack2 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "2" },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);
            Assert.True(ack2.Duplicate);
            Assert.False(ack2.IsSuccess());
        }

        // ExpectedStream
        {
            var ack1 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: 1,
                opts: new NatsJSPubOpts { ExpectedStream = $"{prefix}s1" },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: 2,
                opts: new NatsJSPubOpts { ExpectedStream = "non-existent-stream" },
                cancellationToken: cts.Token);
            Assert.Equal(400, ack2.Error?.Code);
            Assert.Equal(10060, ack2.Error?.ErrCode);
            Assert.Equal("expected stream does not match", ack2.Error?.Description);
            Assert.False(ack2.IsSuccess());
        }

        // ExpectedLastSequence
        {
            var ack1 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: 1,
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: 2,
                opts: new NatsJSPubOpts { ExpectedLastSequence = ack1.Seq },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);

            var ack3 = await js.PublishAsync(
                subject: $"{prefix}s1.foo",
                data: 3,
                opts: new NatsJSPubOpts { ExpectedLastSequence = ack1.Seq },
                cancellationToken: cts.Token);
            Assert.Equal(400, ack3.Error?.Code);
            Assert.Equal(10071, ack3.Error?.ErrCode);
            Assert.Matches(@"wrong last sequence: \d+", ack3.Error?.Description);
        }

        // ExpectedLastSubjectSequence
        {
            var ack1 = await js.PublishAsync(
                subject: $"{prefix}s1.foo.ExpectedLastSubjectSequence",
                data: 1,
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: $"{prefix}s1.foo.ExpectedLastSubjectSequence",
                data: 2,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = ack1.Seq },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);

            var ack3 = await js.PublishAsync(
                subject: $"{prefix}s1.foo.ExpectedLastSubjectSequence",
                data: 3,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = ack1.Seq },
                cancellationToken: cts.Token);
            Assert.Equal(400, ack3.Error?.Code);
            Assert.Equal(10071, ack3.Error?.ErrCode);
            Assert.Matches(@"wrong last sequence: \d+", ack3.Error?.Description);
        }

        // ExpectedLastMsgId
        {
            var ack1 = await js.PublishAsync(
                subject: $"{prefix}s1.foo.ExpectedLastSubjectSequence",
                data: 1,
                opts: new NatsJSPubOpts { MsgId = "ExpectedLastMsgId-1" },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: $"{prefix}s1.foo.ExpectedLastSubjectSequence",
                data: 2,
                opts: new NatsJSPubOpts { MsgId = "ExpectedLastMsgId-2", ExpectedLastMsgId = "ExpectedLastMsgId-1" },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);

            var ack3 = await js.PublishAsync(
                subject: $"{prefix}s1.foo.ExpectedLastSubjectSequence",
                data: 3,
                opts: new NatsJSPubOpts { MsgId = "ExpectedLastMsgId-3", ExpectedLastMsgId = "unexpected-msg-id" },
                cancellationToken: cts.Token);
            Assert.Equal(400, ack3.Error?.Code);
            Assert.Equal(10070, ack3.Error?.ErrCode);
            Assert.Equal("wrong last msg ID: ExpectedLastMsgId-2", ack3.Error?.Description);
        }
    }

    [Fact]
    public async Task Publish_retry_test()
    {
        var retryCount = 0;
        var logger = new InMemoryTestLoggerFactory(LogLevel.Debug, log =>
        {
            if (log is { LogLevel: LogLevel.Debug } && log.EventId == NatsJSLogEvents.PublishNoResponseRetry)
            {
                Interlocked.Increment(ref retryCount);
            }
        });

        var proxy = _server.CreateProxy();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"nats://127.0.0.1:{proxy.Port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = TimeSpan.FromSeconds(3), // give enough time for retries to avoid NatsJSPublishNoResponseExceptions
            LoggerFactory = logger,
        });
        var prefix = _server.GetNextId();

        var ackRegex = new Regex($$"""{"stream":"{{prefix}}s1",\s*"seq":\s*\d+}""");

        var swallowAcksCount = 0;
        proxy.ServerInterceptors.Add(m =>
        {
            if (m != null && ackRegex.IsMatch(m))
            {
                if (Interlocked.Decrement(ref swallowAcksCount) < 0)
                    return m;

                return null;
            }

            return m;
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // use different connection to create stream and consumer to avoid request timeouts
        await using var nats0 = _server.CreateNatsConnection();

        await nats.ConnectRetryAsync();
        await nats0.ConnectRetryAsync();

        var js0 = new NatsJSContext(nats0);
        await js0.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.>" }, cts.Token);
        await js0.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var js = new NatsJSContext(nats);

        // Publish succeeds without retry
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 2 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();

            Assert.Equal(0, Volatile.Read(ref retryCount));

            await Retry.Until("ack received", () => proxy.Frames.Any(f => ackRegex.IsMatch(f.Message)));
        }

        // Publish fails once but succeeds after retry
        {
            await proxy.FlushFramesAsync(nats);
            Interlocked.Exchange(ref retryCount, 0);
            Interlocked.Exchange(ref swallowAcksCount, 1);

            var ack = await js.PublishAsync($"{prefix}s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 2 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();

            Assert.Equal(1, Volatile.Read(ref retryCount));
            await Retry.Until("ack received", () => proxy.Frames.Count(f => ackRegex.IsMatch(f.Message)) == 2, timeout: TimeSpan.FromSeconds(20));
        }

        // Publish fails twice but succeeds after a third retry when attempts is 3
        {
            await proxy.FlushFramesAsync(nats);
            Interlocked.Exchange(ref retryCount, 0);
            Interlocked.Exchange(ref swallowAcksCount, 2);

            var ack = await js.PublishAsync($"{prefix}s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 3 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();

            Assert.Equal(2, Volatile.Read(ref retryCount));
            await Retry.Until("ack received", () => proxy.Frames.Count(f => ackRegex.IsMatch(f.Message)) == 3, timeout: TimeSpan.FromSeconds(20));
        }

        // Publish fails even after two retries
        {
            await proxy.FlushFramesAsync(nats);
            Interlocked.Exchange(ref retryCount, 0);
            Interlocked.Exchange(ref swallowAcksCount, 2);

            await Assert.ThrowsAsync<NatsJSPublishNoResponseException>(async () =>
                await js.PublishAsync($"{prefix}s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 2 }, cancellationToken: cts.Token));

            Assert.Equal(2, Volatile.Read(ref retryCount));
            await Retry.Until("ack received", () => proxy.Frames.Count(f => ackRegex.IsMatch(f.Message)) == 2, timeout: TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Publish_no_responders()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = nats.CreateJetStreamContext();
        var result = await js.TryPublishAsync("foo", 1);
        Assert.IsType<NatsJSPublishNoResponseException>(result.Error);
    }
}
