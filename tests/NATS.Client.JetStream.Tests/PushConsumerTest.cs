using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PushConsumerTest(NatsServerFixture server)
{
    [Fact]
    public async Task Create_push_consumer_config_mapped()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            stream: $"{prefix}s1",
            opts: new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                FilterSubject = $"{prefix}s1.foo",
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                IdleHeartbeat = TimeSpan.FromSeconds(10),
                FlowControl = true,
            },
            cancellationToken: cts.Token);

        var info = consumer.Info;
        Assert.Equal($"{prefix}s1", info.StreamName);
        Assert.Equal($"{prefix}c1", info.Config.Name);
        Assert.Equal($"{prefix}s1.foo", info.Config.FilterSubject);
        Assert.Equal(ConsumerConfigAckPolicy.Explicit, info.Config.AckPolicy);
        Assert.Equal(TimeSpan.FromSeconds(10), info.Config.IdleHeartbeat);
        Assert.True(info.Config.FlowControl);
        Assert.NotNull(info.Config.DeliverSubject);
        Assert.StartsWith("_", info.Config.DeliverSubject);
    }

    [Fact]
    public async Task Create_or_update_push_consumer()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer1 = await js.CreatePushConsumerAsync(
            stream: $"{prefix}s1",
            opts: new NatsJSPushConsumerOpts { Name = $"{prefix}c1", FilterSubject = $"{prefix}s1.a" },
            cancellationToken: cts.Token);

        Assert.Equal($"{prefix}s1.a", consumer1.Info.Config.FilterSubject);

        var consumer2 = await js.CreateOrUpdatePushConsumerAsync(
            stream: $"{prefix}s1",
            opts: new NatsJSPushConsumerOpts { Name = $"{prefix}c1", FilterSubject = $"{prefix}s1.b" },
            cancellationToken: cts.Token);

        Assert.Equal($"{prefix}c1", consumer2.Info.Config.Name);
        Assert.Equal($"{prefix}s1.b", consumer2.Info.Config.FilterSubject);
    }

    [Fact]
    public async Task Get_push_consumer()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1" }, cts.Token);

        var consumer = await js.GetPushConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        Assert.Equal($"{prefix}c1", consumer.Info.Config.Name);
        Assert.Equal($"{prefix}s1", consumer.Info.StreamName);
    }

    [Fact]
    public async Task Get_push_consumer_non_push_throws()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);

        var ex = await Assert.ThrowsAsync<NatsJSException>(
            () => js.GetPushConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token).AsTask());
        Assert.Contains("doesn't have a deliver subject", ex.Message);
    }

    [Theory]
    [InlineData("Invalid.DotName")]
    [InlineData("Invalid SpaceName")]
    [InlineData(null)]
    public async Task Create_push_consumer_invalid_stream_throws(string? streamName)
    {
        var js = new NatsJSContext(new NatsConnection());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await js.CreatePushConsumerAsync(streamName!, cancellationToken: CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await js.CreateOrUpdatePushConsumerAsync(streamName!, cancellationToken: CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await js.GetPushConsumerAsync(streamName!, "c", CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await js.CreateOrderedPushConsumerAsync(streamName!, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Push_consume_msgs()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 30; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.foo", new TestData { Test = i }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1" }, cts.Token);
        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count, msg.Data!.Test);
            count++;
            if (count == 30)
                break;
        }

        Assert.Equal(30, count);
    }

    [Fact]
    public async Task Push_consume_filter_subject()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        for (var i = 0; i < 5; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        for (var i = 0; i < 5; i++)
            await js.PublishAsync($"{prefix}s1.bar", i + 100, cancellationToken: cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", FilterSubject = $"{prefix}s1.bar" },
            cts.Token);

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count + 100, msg.Data);
            count++;
            if (count == 5)
                break;
        }

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task Push_consume_fetch_throws()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1" }, cts.Token);

        var ex = Assert.Throws<NatsJSProtocolException>(() => consumer.FetchAsync<int>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cts.Token));
        Assert.Equal("Consumer is push based", ex.HeaderMessageText);

        var ex2 = await Assert.ThrowsAsync<NatsJSProtocolException>(async () => await consumer.NextAsync<int>(cancellationToken: cts.Token));
        Assert.Equal("Consumer is push based", ex2.HeaderMessageText);

        var ex3 = Assert.Throws<NatsJSProtocolException>(() => consumer.FetchNoWaitAsync<int>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cts.Token));
        Assert.Equal("Consumer is push based", ex3.HeaderMessageText);
    }

    [Fact]
    public async Task Push_consume_delete_then_use_throws()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = (NatsJSPushConsumer)await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1" }, cts.Token);

        await consumer.DeleteAsync(cts.Token);

        await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await foreach (var unused in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task Push_consume_cancel()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 5; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1" }, cts.Token);

        var count = 0;
        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: consumeCts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            Assert.Equal(count, msg.Data);
            count++;

            if (count == 3)
                consumeCts.Cancel();
        }

        Assert.Equal(3, count);
    }
}
