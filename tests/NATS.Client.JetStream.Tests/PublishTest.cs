using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

public class PublishTest
{
    private readonly ITestOutputHelper _output;

    public PublishTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Publish_test()
    {
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync("s1", new[] { "s1.>" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        // Publish
        {
            var ack = await js.PublishAsync(
                "s1.foo",
                new TestData
                {
                    Test = 1,
                },
                serializer: TestDataJsonSerializer<TestData>.Default,
                cancellationToken: cts.Token);
            Assert.Null(ack.Error);
            Assert.Equal(1, (int)ack.Seq);
            Assert.Equal("s1", ack.Stream);
            Assert.False(ack.Duplicate);
            Assert.True(ack.IsSuccess());
        }

        // Duplicate
        {
            var ack1 = await js.PublishAsync(
                subject: "s1.foo",
                data: new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "2" },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);
            Assert.Equal(2, (int)ack1.Seq);
            Assert.False(ack1.Duplicate);
            Assert.True(ack1.IsSuccess());

            var ack2 = await js.PublishAsync(
                subject: "s1.foo",
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
                subject: "s1.foo",
                data: 1,
                opts: new NatsJSPubOpts { ExpectedStream = "s1" },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: "s1.foo",
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
                subject: "s1.foo",
                data: 1,
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: "s1.foo",
                data: 2,
                opts: new NatsJSPubOpts { ExpectedLastSequence = ack1.Seq },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);

            var ack3 = await js.PublishAsync(
                subject: "s1.foo",
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
                subject: "s1.foo.ExpectedLastSubjectSequence",
                data: 1,
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: "s1.foo.ExpectedLastSubjectSequence",
                data: 2,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = ack1.Seq },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);

            var ack3 = await js.PublishAsync(
                subject: "s1.foo.ExpectedLastSubjectSequence",
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
                subject: "s1.foo.ExpectedLastSubjectSequence",
                data: 1,
                opts: new NatsJSPubOpts { MsgId = "ExpectedLastMsgId-1" },
                cancellationToken: cts.Token);
            Assert.Null(ack1.Error);

            var ack2 = await js.PublishAsync(
                subject: "s1.foo.ExpectedLastSubjectSequence",
                data: 2,
                opts: new NatsJSPubOpts { MsgId = "ExpectedLastMsgId-2", ExpectedLastMsgId = "ExpectedLastMsgId-1" },
                cancellationToken: cts.Token);
            Assert.Null(ack2.Error);

            var ack3 = await js.PublishAsync(
                subject: "s1.foo.ExpectedLastSubjectSequence",
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
        var ackRegex = new Regex(@"{""stream"":""s1"",\s*""seq"":\d+}");

        // give enough time for retries to avoid NatsJSPublishNoResponseExceptions
        var natsOpts = NatsOpts.Default with { RequestTimeout = TimeSpan.FromSeconds(3) };

        await using var server = await NatsServer.StartJSAsync();
        var (nats1, proxy) = server.CreateProxiedClientConnection(natsOpts);
        await using var nats = nats1;

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

        var retryCount = 0;
        server.OnLog += log =>
        {
            if (log is { LogLevel: LogLevel.Debug } && log.EventId == NatsJSLogEvents.PublishNoResponseRetry)
            {
                Interlocked.Increment(ref retryCount);
            }
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // use different connection to create stream and consumer to avoid request timeouts
        await using var nats0 = await server.CreateClientConnectionAsync();

        await nats.ConnectRetryAsync();
        await nats0.ConnectRetryAsync();

        var js0 = new NatsJSContext(nats0);
        await js0.CreateStreamAsync("s1", new[] { "s1.>" }, cts.Token);
        await js0.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        var js = new NatsJSContext(nats);

        // Publish succeeds without retry
        {
            var ack = await js.PublishAsync("s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 2 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();

            Assert.Equal(0, Volatile.Read(ref retryCount));

            await Retry.Until("ack received", () => proxy.Frames.Any(f => ackRegex.IsMatch(f.Message)));
        }

        // Publish fails once but succeeds after retry
        {
            await proxy.FlushFramesAsync(nats);
            Interlocked.Exchange(ref retryCount, 0);
            Interlocked.Exchange(ref swallowAcksCount, 1);

            var ack = await js.PublishAsync("s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 2 }, cancellationToken: cts.Token);
            ack.EnsureSuccess();

            Assert.Equal(1, Volatile.Read(ref retryCount));
            await Retry.Until("ack received", () => proxy.Frames.Count(f => ackRegex.IsMatch(f.Message)) == 2, timeout: TimeSpan.FromSeconds(20));
        }

        // Publish fails twice but succeeds after a third retry when attempts is 3
        {
            await proxy.FlushFramesAsync(nats);
            Interlocked.Exchange(ref retryCount, 0);
            Interlocked.Exchange(ref swallowAcksCount, 2);

            var ack = await js.PublishAsync("s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 3 }, cancellationToken: cts.Token);
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
                await js.PublishAsync("s1.foo", 1, opts: new NatsJSPubOpts { RetryAttempts = 2 }, cancellationToken: cts.Token));

            Assert.Equal(2, Volatile.Read(ref retryCount));
            await Retry.Until("ack received", () => proxy.Frames.Count(f => ackRegex.IsMatch(f.Message)) == 2, timeout: TimeSpan.FromSeconds(20));
        }
    }
}
