using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;

// ReSharper disable MethodHasAsyncOverload
namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class CancellationTokenTests(NatsServerFixture server)
{
    [Fact]
    public async Task FetchAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in consumer.FetchAsync(new NatsJSFetchOpts { MaxMsgs = 10 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cancelledCts.Token))
            {
            }
        });

        // Verify no messages were reserved as pending
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task FetchNoWaitAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 5; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in consumer.FetchNoWaitAsync(new NatsJSFetchOpts { MaxMsgs = 5 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cancelledCts.Token))
            {
            }
        });

        // Verify no messages were reserved as pending
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task NextAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = 1 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
        ack.EnsureSuccess();

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await consumer.NextAsync(serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cancelledCts.Token);
        });

        // Verify no messages were reserved as pending
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task ConsumeAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        for (var i = 0; i < 5; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cancelledCts.Token))
            {
            }
        });

        // Verify no messages were reserved as pending
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    [Fact]
    public async Task OrderedConsumer_FetchAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 5; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in consumer.FetchAsync<int>(new NatsJSFetchOpts { MaxMsgs = 5 }, cancellationToken: cancelledCts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task OrderedConsumer_FetchNoWaitAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 5; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in consumer.FetchNoWaitAsync<int>(new NatsJSFetchOpts { MaxMsgs = 5 }, cancellationToken: cancelledCts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task OrderedConsumer_ConsumeAsync_with_cancelled_token_throws_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var js = new NatsJSContext(nats);
        var stream = await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 5; i++)
        {
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        }

        var consumer = await stream.CreateOrderedConsumerAsync(cancellationToken: cts.Token);

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in consumer.ConsumeAsync<int>(cancellationToken: cancelledCts.Token))
            {
            }
        });
    }
}
