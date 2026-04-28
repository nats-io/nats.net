using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;

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

    // Canary for the dispose race in JetStream fetch/consume/ordered-consume:
    // messages in-flight when DisposeAsync is called must all reach the
    // channel before it completes. If this test starts flapping, check the
    // DrainAsync pattern in NatsSubBase and the DisposeAsync overrides in
    // NatsJSFetch, NatsJSConsume, NatsJSOrderedConsume.
    [Theory]

    // TODO: Fix this test
    // [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Fetch_dispose_test(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            RequestReplyMode = mode,
            DrainSubscriptionsOnDispose = true,
        });
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

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
    public async Task Fetch_connection_dispose_drains_buffered_messages()
    {
        const int totalMsgs = 100;
        const int pullBatch = 20;
        const int bailAt = 10;

        var prefix = _server.GetNextId();
        var streamName = $"{prefix}s1";
        var subject = $"{prefix}s1.x";
        var consumerName = $"{prefix}c1";

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await using (var setup = new NatsConnection(new NatsOpts { Url = _server.Url }))
        {
            var setupJs = new NatsJSContext(setup);
            var stream = await setupJs.CreateStreamAsync(new StreamConfig(streamName, [subject]), cts.Token);

            for (var i = 0; i < totalMsgs; i++)
                (await setupJs.PublishAsync(subject, $"msg-{i}", cancellationToken: cts.Token)).EnsureSuccess();

            await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig(consumerName), cts.Token);
        }

        var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            DrainSubscriptionsOnDispose = true,
            ConsumerDrainOnDisposeTimeout = TimeSpan.FromSeconds(30),
        });
        var js = new NatsJSContext(nats);
        var consumer = await js.GetConsumerAsync(streamName, consumerName, cts.Token);

        var reachedBail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchTask = Task.Run(
            async () =>
            {
                var count = 0;
                var fetchOpts = new NatsJSFetchOpts { MaxMsgs = pullBatch, Expires = TimeSpan.FromSeconds(30) };
                await foreach (var msg in consumer.FetchAsync<string>(fetchOpts, cancellationToken: cts.Token))
                {
                    count++;
                    await msg.AckAsync(cancellationToken: cts.Token);
                    if (count >= bailAt)
                        reachedBail.TrySetResult();
                    await Task.Delay(50, cts.Token);
                }

                return count;
            },
            cts.Token);

        await reachedBail.Task.WaitAsync(cts.Token);

        await nats.DisposeAsync();

        var consumed = await fetchTask;

        await using var check = new NatsConnection(new NatsOpts { Url = _server.Url });
        var checkJs = new NatsJSContext(check);
        var info = (await checkJs.GetConsumerAsync(streamName, consumerName, cts.Token)).Info;

        Assert.True(consumed >= bailAt, $"consumed {consumed} should be >= {bailAt}");
        Assert.Equal(0, info.NumAckPending);
        Assert.Equal((ulong)consumed, (ulong)info.AckFloor.ConsumerSeq);
        Assert.Equal((ulong)(totalMsgs - consumed), info.NumPending);
    }
}
