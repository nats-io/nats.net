using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ConsumerFetchTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ConsumerFetchTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Fetch_test(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var count = 0;
        await using var fc =
            await consumer.FetchInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, opts: new NatsJSFetchOpts { MaxMsgs = 10 }, cancellationToken: cts.Token);
        await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task FetchNoWait_test(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", new[] { $"{prefix}s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = (NatsJSConsumer)await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var count = 0;
        await foreach (var msg in consumer.FetchNoWaitAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, opts: new NatsJSFetchOpts { MaxMsgs = 10 }, cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
        }

        Assert.Equal(10, count);
    }

    [Theory]

    // TODO: Fix this test
    // [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Fetch_dispose_test(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();

        // var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cts = new CancellationTokenSource();

        var js = new NatsJSContext(nats);

        var streamName = $"{prefix}s1";
        var subjectSub = $"{prefix}s1.*";
        var consumerName = $"{prefix}c1";
        var subject = $"{prefix}s1.foo";

        await js.CreateStreamAsync(streamName, [subjectSub], cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync(streamName, consumerName, cancellationToken: cts.Token);

        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
            IdleHeartbeat = TimeSpan.FromSeconds(5),
            Expires = TimeSpan.FromSeconds(10),
        };

        for (var i = 0; i < 100; i++)
        {
            var ack = await js.PublishAsync(subject, new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var fc = await consumer.FetchInternalAsync<TestData>(serializer: TestDataJsonSerializer<TestData>.Default, opts: fetchOpts, cancellationToken: cts.Token);

        var signal1 = new WaitSignal();
        var signal2 = new WaitSignal();
        var reader = Task.Run(async () =>
        {
            var x = 0;
            await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
            {
                _output.WriteLine($"rcv:{++x}");
                await msg.AckAsync(cancellationToken: cts.Token);
                signal1.Pulse();
                await signal2;
            }
        });

        await signal1;

        // Dispose waits for all the pending messages to be delivered to the loop
        // since the channel reader carries on reading the messages in its internal queue.
        await fc.DisposeAsync();

        // At this point we should only have ACKed one message
        await Retry.Until(
            "ack pending 9",
            async () =>
            {
                var c = await js.GetConsumerAsync(streamName, consumerName, cts.Token);
                _output.WriteLine($"pend1:{c.Info.NumAckPending}");
                return c.Info.NumAckPending == 9;
            },
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(30));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(9, consumer.Info.NumAckPending);

        signal2.Pulse();

        await reader;

        await Retry.Until(
            "ack pending 0",
            async () =>
            {
                var c = await js.GetConsumerAsync(streamName, consumerName, cts.Token);
                _output.WriteLine($"pend:{c.Info.NumAckPending}");
                return c.Info.NumAckPending == 0;
            },
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(30));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task Fetch_dispose_does_not_drop_buffered_messages()
    {
        // Reproduce the race: messages delivered by server but dropped during
        // dispose because the channel is completed before all writes finish.
        // Strategy: pump thousands of messages so there are always messages
        // in-flight during dispose, then check server stats for the gap.
        // Run iterations in parallel (4x CPU count) to maximize contention.
        var iterations = Environment.ProcessorCount * 4;

        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var js = new NatsJSContext(nats);
        var streamName = $"{prefix}s1";
        var subject = $"{prefix}s1.foo";

        await js.CreateStreamAsync(streamName, new[] { $"{prefix}s1.*" }, cts.Token);

        // Keep publishing messages in the background throughout the test
        // to ensure there are always messages in-flight during dispose.
        var publishTask = Task.Run(
            async () =>
            {
                var seq = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    for (var i = 0; i < 10_000; i++)
                    {
                        var ack = await js.PublishAsync(
                            subject,
                            new TestData { Test = seq++ },
                            serializer: TestDataJsonSerializer<TestData>.Default,
                            cancellationToken: cts.Token);
                        ack.EnsureSuccess();
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token).ConfigureAwait(false);
                }
            },
            cts.Token);

        // Wait for first batch to land before starting consumers.
        await Retry.Until(
            "initial messages published",
            async () =>
            {
                var stream = await js.GetStreamAsync(streamName, cancellationToken: cts.Token);
                return stream.Info.State.Messages >= 10_000;
            },
            timeout: TimeSpan.FromSeconds(30));

        var tasks = Enumerable.Range(0, iterations).Select(iteration => Task.Run(async () =>
        {
            var consumerName = $"{prefix}c{iteration}";
            var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync(
                streamName,
                consumerName,
                cancellationToken: cts.Token);

            var fetchOpts = new NatsJSFetchOpts
            {
                MaxMsgs = 100_000,
                IdleHeartbeat = TimeSpan.FromSeconds(5),
                Expires = TimeSpan.FromSeconds(30),
            };

            var fc = await consumer.FetchInternalAsync<TestData>(
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: fetchOpts,
                cancellationToken: cts.Token);

            // Wait until some messages arrive but not all — we want
            // messages still in-flight when we dispose.
            await Retry.Until(
                "some messages in channel",
                () => fc.Msgs.Count >= 10,
                timeout: TimeSpan.FromSeconds(30));

            // Dispose while messages are still being delivered.
            await fc.DisposeAsync();

            // Read and ack everything that made it into the channel.
            var received = 0;
            await foreach (var msg in fc.Msgs.ReadAllAsync(cts.Token))
            {
                received++;
                await msg.AckAsync(cancellationToken: cts.Token);
            }

            // Check server-side: delivered - acked should be zero.
            // If messages were delivered but dropped before reaching the
            // channel, NumAckPending > 0 (server sent them, we never acked).
            await consumer.RefreshAsync(cts.Token);
            var delivered = consumer.Info.Delivered.ConsumerSeq;
            var ackFloor = consumer.Info.AckFloor.ConsumerSeq;
            var ackPending = consumer.Info.NumAckPending;

            _output.WriteLine($"iter:{iteration} received:{received} delivered:{delivered} ackFloor:{ackFloor} ackPending:{ackPending}");

            Assert.Equal(0, ackPending);
        })).ToArray();

        await Task.WhenAll(tasks);

        // Stop the background publisher.
        cts.Cancel();
        try
        {
            await publishTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
