using NATS.Client.Core.Tests;
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

        var deliverSubject = js.NewBaseInbox();
        var consumer = await js.CreatePushConsumerAsync(
            stream: $"{prefix}s1",
            opts: new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                DeliverSubject = deliverSubject,
                FilterSubject = $"{prefix}s1.foo",
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                IdleHeartbeat = TimeSpan.FromSeconds(10),
                FlowControl = true,
                InactiveThreshold = TimeSpan.FromMinutes(5),
            },
            cancellationToken: cts.Token);

        var info = consumer.Info;
        Assert.Equal($"{prefix}s1", info.StreamName);
        Assert.Equal($"{prefix}c1", info.Config.Name);
        Assert.Equal($"{prefix}s1.foo", info.Config.FilterSubject);
        Assert.Equal(ConsumerConfigAckPolicy.Explicit, info.Config.AckPolicy);
        Assert.Equal(TimeSpan.FromSeconds(10), info.Config.IdleHeartbeat);
        Assert.True(info.Config.FlowControl);
        Assert.Equal(TimeSpan.FromMinutes(5), info.Config.InactiveThreshold);
        Assert.Equal(deliverSubject, info.Config.DeliverSubject);
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
            opts: new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), FilterSubject = $"{prefix}s1.a" },
            cancellationToken: cts.Token);

        Assert.Equal($"{prefix}s1.a", consumer1.Info.Config.FilterSubject);

        var consumer2 = await js.CreateOrUpdatePushConsumerAsync(
            stream: $"{prefix}s1",
            opts: new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), FilterSubject = $"{prefix}s1.b" },
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
        await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() }, cts.Token);

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
    public async Task Create_push_consumer_without_deliver_subject_throws()
    {
        var js = new NatsJSContext(new NatsConnection());

        await Assert.ThrowsAsync<NatsJSException>(
            () => js.CreatePushConsumerAsync("stream", new NatsJSPushConsumerOpts { Name = "c1" }).AsTask());
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

        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() }, cts.Token);
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
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), FilterSubject = $"{prefix}s1.bar" },
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
        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() }, cts.Token);

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
        var consumer = (NatsJSPushConsumer)await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() }, cts.Token);

        await consumer.DeleteAsync(cts.Token);

        await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await foreach (var unused in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task Push_consumer_ephemeral_auto_deleted()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() },
            cts.Token);

        var name = consumer.Info.Config.Name;
        Assert.Equal($"{prefix}c1", name);

        await Task.Delay(TimeSpan.FromSeconds(8), cts.Token);

        var ex = await Assert.ThrowsAsync<NatsJSApiException>(
            () => js.GetPushConsumerAsync($"{prefix}s1", name!, cts.Token).AsTask());
        Assert.Equal(10014, ex.Error.ErrCode);
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

        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() }, cts.Token);

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

    [Fact]
    public async Task Push_consume_deliver_group()
    {
        await using var nats1 = server.CreateNatsConnection();
        await using var nats2 = server.CreateNatsConnection();
        await using var nats3 = server.CreateNatsConnection();
        await Task.WhenAll(nats1.ConnectRetryAsync(), nats2.ConnectRetryAsync(), nats3.ConnectRetryAsync());
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats1);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var opts = new NatsJSPushConsumerOpts
        {
            Name = $"{prefix}c1",
            DeliverSubject = js.NewBaseInbox(),
            DeliverGroup = $"{prefix}workers",
            AckWait = TimeSpan.FromSeconds(30),
        };
        var consumer1 = (NatsJSPushConsumer)await js.CreatePushConsumerAsync($"{prefix}s1", opts, cts.Token);

        const int total = 51;
        for (var i = 0; i < total; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var js2 = new NatsJSContext(nats2);
        var js3 = new NatsJSContext(nats3);
        var consumer2 = (NatsJSPushConsumer)await js2.GetPushConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var consumer3 = (NatsJSPushConsumer)await js3.GetPushConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);

        var result = new int[total];
        var totalCount = 0;
        var consumeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        async Task Worker(INatsJSPushConsumer c)
        {
            await foreach (var msg in c.ConsumeAsync<int>(cancellationToken: consumeCts.Token))
            {
                Interlocked.Increment(ref result[msg.Data]);
                Interlocked.Increment(ref totalCount);
                await msg.AckAsync(cancellationToken: cts.Token);
            }
        }

        await Task.WhenAll(Worker(consumer1), Worker(consumer2), Worker(consumer3));

        for (var i = 0; i < total; i++)
            Assert.True(result[i] >= 1, $"Message {i} was received {result[i]} times (expected at least 1)");

        Assert.True(totalCount >= total, $"Expected at least {total}, got {totalCount}");
    }

    [Fact]
    public async Task Push_consume_ack_none()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), AckPolicy = ConsumerConfigAckPolicy.None },
            cts.Token);

        for (var i = 0; i < 10; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            Assert.Equal(count, msg.Data);
            count++;
            if (count == 10)
                break;
        }

        Assert.Equal(10, count);
    }

    [Fact]
    public async Task Push_consume_max_deliver_redelivery()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = (NatsJSPushConsumer)await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                DeliverSubject = js.NewBaseInbox(),
                AckWait = TimeSpan.FromMilliseconds(100),
                MaxDeliver = 5,
            },
            cts.Token);

        await js.PublishAsync($"{prefix}s1.foo", 42, cancellationToken: cts.Token);

        var deliveries = 0;
        var consumeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: consumeCts.Token))
        {
            deliveries++;
            if (deliveries == 5)
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                break;
            }

            // Don't ack — wait for redelivery after AckWait
        }

        Assert.True(deliveries >= 5, $"Expected at least 5 deliveries, got {deliveries}");
    }

    [Fact]
    public async Task Push_consume_late_ack_redelivery()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = (NatsJSPushConsumer)await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), AckWait = TimeSpan.FromMilliseconds(300) },
            cts.Token);

        await js.PublishAsync($"{prefix}s1.foo", 99, cancellationToken: cts.Token);

        var deliveries = 0;
        var consumeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in consumer.ConsumeAsync<int>(cancellationToken: consumeCts.Token))
        {
            deliveries++;
            if (deliveries == 1)
            {
                // Wait past AckWait without acking → server redelivers
                await Task.Delay(TimeSpan.FromMilliseconds(600), cts.Token);
            }
            else if (deliveries == 2)
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                break;
            }
        }

        Assert.True(deliveries >= 2, $"Expected at least 2 deliveries, got {deliveries}");
    }

    [Fact]
    public async Task Push_unpin_throws()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        var consumer = await js.CreatePushConsumerAsync($"{prefix}s1", new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox() }, cts.Token);

        await Assert.ThrowsAsync<NatsJSProtocolException>(async () => await consumer.UnpinAsync("group", cts.Token));
    }

    [Fact]
    public async Task Push_consume_headers_only()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), HeadersOnly = true },
            cts.Token);

        for (var i = 0; i < 5; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: cts.Token))
        {
            Assert.NotNull(msg.Headers);
            count++;
            await msg.AckAsync(cancellationToken: cts.Token);
            if (count == 5)
                break;
        }

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task Push_consume_deliver_policy_by_start_seq()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 10; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        // Consumer sequence is 1-based: OptStartSeq=5 → start from 5th message
        var pushConsumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                DeliverSubject = js.NewBaseInbox(),
                DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
                OptStartSeq = 5,
            },
            cts.Token);

        var count = 0;
        await foreach (var msg in pushConsumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            Assert.Equal(count + 4, msg.Data);
            await msg.AckAsync(cancellationToken: cts.Token);
            count++;
            if (count == 6)
                break;
        }

        Assert.Equal(6, count);
    }

    [Fact]
    public async Task Push_consume_deliver_policy_last()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        for (var i = 0; i < 10; i++)
            await js.PublishAsync($"{prefix}s1.foo", i, cancellationToken: cts.Token);

        // DeliverPolicy=Last → only the most recent message
        var pushConsumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts { Name = $"{prefix}c1", DeliverSubject = js.NewBaseInbox(), DeliverPolicy = ConsumerConfigDeliverPolicy.Last },
            cts.Token);

        var count = 0;
        await foreach (var msg in pushConsumer.ConsumeAsync<int>(cancellationToken: cts.Token))
        {
            Assert.Equal(9, msg.Data);
            await msg.AckAsync(cancellationToken: cts.Token);
            count++;
            if (count == 1)
                break;
        }

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Push_consume_timeout_notification_periodic()
    {
        var proxy = new NatsProxy(server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                DeliverSubject = js.NewBaseInbox(),
                IdleHeartbeat = TimeSpan.FromSeconds(1),
            },
            cts.Token);

        var timeouts = 0;
        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consumeOpts = new NatsJSConsumeOpts
        {
            NotificationHandler = (notification, _) =>
            {
                if (notification is NatsJSTimeoutNotification)
                    Interlocked.Increment(ref timeouts);
                return Task.CompletedTask;
            },
        };

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: consumeCts.Token))
            {
                _ = msg;
            }
        });

        // Let the consumer start so the timeout timer is armed.
        await Task.Delay(1_000, cts.Token);

        // Swallow heartbeats so the client sees silence and the timeout notification fires.
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false ? null : m);

        // Idle heartbeat is 1s, so the timeout notification fires every ~2s of silence;
        // waiting for two notifications proves the timer re-arms itself periodically.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (Volatile.Read(ref timeouts) < 2)
        {
            Assert.True(DateTime.UtcNow < deadline, $"timed out waiting for the notifications, got {timeouts}");
            await Task.Delay(100, cts.Token);
        }

        consumeCts.Cancel();
        await Task.WhenAny(consumeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(Volatile.Read(ref timeouts) >= 2);
    }

    [Fact]
    public async Task Push_consume_no_timeout_notification_without_idle_heartbeat()
    {
        await using var nats = server.CreateNatsConnection();
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                DeliverSubject = js.NewBaseInbox(),

                // No IdleHeartbeat: the server sends no heartbeats and the client
                // must not arm the heartbeat timer nor emit timeout notifications.
            },
            cts.Token);

        var timeouts = 0;
        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consumeOpts = new NatsJSConsumeOpts
        {
            NotificationHandler = (notification, _) =>
            {
                if (notification is NatsJSTimeoutNotification)
                    Interlocked.Increment(ref timeouts);
                return Task.CompletedTask;
            },
        };

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: consumeCts.Token))
            {
                _ = msg;
            }
        });

        // Nothing is published and the server sends no heartbeats. Wait longer than
        // the 10s timeout a wrongly armed default (2x5s) timer would produce.
        await Task.Delay(TimeSpan.FromSeconds(13), cts.Token);
        Assert.Equal(0, Volatile.Read(ref timeouts));

        consumeCts.Cancel();
        await Task.WhenAny(consumeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref timeouts));
    }

    [Fact]
    public async Task Push_consume_unhandled_control_message_notified()
    {
        var proxy = new NatsProxy(server.Port);
        await using var nats = new NatsConnection(new NatsOpts { Url = $"nats://127.0.0.1:{proxy.Port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await nats.ConnectRetryAsync();
        var prefix = server.GetNextId();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var consumer = await js.CreatePushConsumerAsync(
            $"{prefix}s1",
            new NatsJSPushConsumerOpts
            {
                Name = $"{prefix}c1",
                DeliverSubject = js.NewBaseInbox(),
                IdleHeartbeat = TimeSpan.FromSeconds(1),
            },
            cts.Token);

        var notified = 0;
        var consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consumeOpts = new NatsJSConsumeOpts
        {
            NotificationHandler = (notification, _) =>
            {
                if (notification is NatsJSProtocolNotification { HeaderCode: 409, HeaderMessageText: "Idle Heartbeat" })
                    Interlocked.Increment(ref notified);
                return Task.CompletedTask;
            },
        };

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: consumeOpts, cancellationToken: consumeCts.Token))
            {
                _ = msg;
            }
        });

        // Let the consumer subscribe, then rewrite idle heartbeats into a non-terminal
        // 409 control message like the one the server sends on shutdown or leadership
        // change: it must reach the notification handler, not be swallowed by a log.
        // Only the status code is rewritten so the frame lengths stay valid.
        await Task.Delay(1_000, cts.Token);
        proxy.ServerInterceptors.Add(m => m?.Contains("Idle Heartbeat") ?? false
            ? m.Replace("NATS/1.0 100", "NATS/1.0 409")
            : m);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (Volatile.Read(ref notified) < 1)
        {
            Assert.True(DateTime.UtcNow < deadline, $"timed out waiting for the protocol notification, got {notified}");
            await Task.Delay(100, cts.Token);
        }

        consumeCts.Cancel();
        await Task.WhenAny(consumeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(Volatile.Read(ref notified) >= 1);
    }
}
