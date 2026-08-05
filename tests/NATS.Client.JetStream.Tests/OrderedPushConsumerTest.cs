using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class OrderedPushConsumerTest(NatsServerFixture server)
{
    [Fact]
    public async Task Consume_all_in_order()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 50; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var consumer = (NatsJSOrderedPushConsumer)await js.CreateOrderedPushConsumerAsync($"{prefix}s1", cancellationToken: cts.Token);

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;
            if (count == 50)
                break;
        }

        Assert.Equal(50, count);
    }

    [Fact]
    public async Task Consume_filter_subjects()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        // Publish to 3 subjects: foo(0-9), bar(100-109), baz(200-209)
        for (var i = 0; i < 10; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);
        for (var i = 0; i < 10; i++)
            await js.PublishAsync($"{prefix}s1.bar", i + 100, cancellationToken: cts.Token);
        for (var i = 0; i < 10; i++)
            await js.PublishAsync($"{prefix}s1.baz", i + 200, cancellationToken: cts.Token);

        var consumer = (NatsJSOrderedPushConsumer)await js.CreateOrderedPushConsumerAsync(
            $"{prefix}s1",
            new NatsJSOrderedConsumerOpts { FilterSubjects = [$"{prefix}s1.foo", $"{prefix}s1.baz"] },
            cts.Token);

        var count = 0;
        var seen = new HashSet<int>();
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            seen.Add(msg.Data);
            count++;
            if (count == 20)
                break;
        }

        Assert.Equal(20, count);

        // foo: 0-9, baz: 200-209
        for (var i = 0; i < 10; i++)
        {
            Assert.Contains(i, seen);
            Assert.Contains(i + 200, seen);
        }

        // bar values (100-109) must not appear
        for (var i = 100; i < 110; i++)
            Assert.DoesNotContain(i, seen);
    }

    [Fact]
    public async Task Fetch_throws()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreateOrderedPushConsumerAsync($"{prefix}s1", cancellationToken: cts.Token);

        Assert.Throws<NatsJSProtocolException>(() => consumer.FetchAsync<int>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cts.Token));
        await Assert.ThrowsAsync<NatsJSProtocolException>(async () => await consumer.NextAsync<int>(cancellationToken: cts.Token));
        Assert.Throws<NatsJSProtocolException>(() => consumer.FetchNoWaitAsync<int>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Consume_reconnect()
    {
        var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var stream = await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);

        async Task PublishWithId(int i)
        {
            for (var j = 0; j < 3; j++)
            {
                PubAckResponse ack;
                try
                {
                    ack = await js.PublishAsync("s1.foo", i, opts: new NatsJSPubOpts { MsgId = $"{i}" }, cancellationToken: cts.Token);
                }
                catch (NatsException)
                {
                    await Task.Delay(100, cts.Token);
                    continue;
                }

                if (ack.IsSuccess())
                    return;
                await Task.Delay(100, cts.Token);
                if (ack.Duplicate)
                    break;
                ack.EnsureSuccess();
            }

            throw new Exception("Publish failed after retries");
        }

        for (var i = 0; i < 50; i++)
        {
            if (i % 10 == 0)
                server = await server.RestartAsync();
            await PublishWithId(i);
        }

        await PublishWithId(-1);

        var consumer = (NatsJSOrderedPushConsumer)await js.CreateOrderedPushConsumerAsync("s1", cancellationToken: cts.Token);

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            if (msg.Data == -1)
                break;
            Assert.Equal(count, msg.Data);
            count++;
        }

        Assert.Equal(50, count);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_deleted_recreate()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 20; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var consumer = (NatsJSOrderedPushConsumer)await js.CreateOrderedPushConsumerAsync($"{prefix}s1", cancellationToken: cts.Token);

        var count = 0;
        var deleted = false;
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;

            // Delete all ephemeral consumers on the stream to trigger recreate
            if (count == 5 && !deleted)
            {
                var names = new List<string>();
                await foreach (var name in js.ListConsumerNamesAsync($"{prefix}s1", cancellationToken: cts.Token))
                    names.Add(name);
                foreach (var name in names)
                    await js.DeleteConsumerAsync($"{prefix}s1", name, cts.Token);
                deleted = true;
            }

            if (count == 20)
                break;
        }

        Assert.Equal(20, count);
    }

    [Fact]
    public async Task Consume_consumer_config_has_inactive_threshold()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 5; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var consumer = (NatsJSOrderedPushConsumer)await js.CreateOrderedPushConsumerAsync($"{prefix}s1", cancellationToken: cts.Token);

        var count = 0;
        var found = false;
        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: consumeCts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;

            // Once the first message is delivered, the internal ephemeral consumer
            // exists on the server. Its config must carry the ordered consumer's
            // InactiveThreshold default (5 minutes) so it survives reconnects.
            if (count == 1)
            {
                var names = new List<string>();
                await foreach (var name in js.ListConsumerNamesAsync($"{prefix}s1", cancellationToken: cts.Token))
                    names.Add(name);

                foreach (var name in names)
                {
                    var info = await js.GetConsumerAsync($"{prefix}s1", name, cts.Token);
                    if (info.Info.Config.FlowControl && info.Info.Config.AckPolicy == ConsumerConfigAckPolicy.None)
                    {
                        Assert.Equal(TimeSpan.FromMinutes(5), info.Info.Config.InactiveThreshold);
                        found = true;
                    }
                }

                consumeCts.Cancel();
                break;
            }
        }

        Assert.True(count >= 1);
        Assert.True(found);
    }
}
