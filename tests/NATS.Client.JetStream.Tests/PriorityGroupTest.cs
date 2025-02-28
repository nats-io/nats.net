using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PriorityGroupTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public PriorityGroupTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Next_from_overflow_group()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.{i}", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerConfig = new ConsumerConfig($"{prefix}c1") { PriorityGroups = ["jobs"], PriorityPolicy = "overflow", };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Err on no group
        {
            var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () => await consumer.NextAsync<int>(cancellationToken: cts.Token));
            Assert.Equal(400, exception.HeaderCode);
            Assert.Equal("Bad Request - Priority Group missing", exception.HeaderMessageText);
        }

        // Assign group
        {
            var opts = new NatsJSNextOpts { PriorityGroup = new NatsJSPriorityGroupOpts { Group = "jobs" } };
            var next = await consumer.NextAsync<int>(opts: opts, cancellationToken: cts.Token);

            if (next is { } msg)
            {
                Assert.Equal(0, msg.Data);
            }
            else
            {
                Assert.Fail("no message");
            }
        }
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Fetch_from_overflow_group()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.{i}", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerConfig = new ConsumerConfig($"{prefix}c1") { PriorityGroups = ["jobs"], PriorityPolicy = "overflow", };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Err on no group
        {
            var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
            {
                var opts = new NatsJSFetchOpts { MaxMsgs = 5 };
                await foreach (var msg in consumer.FetchAsync<int>(opts, cancellationToken: cts.Token))
                {
                }
            });
            Assert.Equal(400, exception.HeaderCode);
            Assert.Equal("Bad Request - Priority Group missing", exception.HeaderMessageText);
        }

        // Assign group
        {
            var opts = new NatsJSFetchOpts { MaxMsgs = 5, PriorityGroup = new NatsJSPriorityGroupOpts { Group = "jobs" } };
            var count = 0;
            await foreach (var msg in consumer.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                Assert.Equal(count++, msg.Data);
            }

            Assert.Equal(5, count);
        }
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Consume_from_overflow_group()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.{i}", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerConfig = new ConsumerConfig($"{prefix}c1") { PriorityGroups = ["jobs"], PriorityPolicy = "overflow", };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Err on no group
        {
            var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
            {
                var opts = new NatsJSConsumeOpts { MaxMsgs = 5 };
                await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
                {
                }
            });
            Assert.Equal(400, exception.HeaderCode);
            Assert.Equal("Bad Request - Priority Group missing", exception.HeaderMessageText);
        }

        // Assign group
        {
            var opts = new NatsJSConsumeOpts { MaxMsgs = 5, PriorityGroup = new NatsJSPriorityGroupOpts { Group = "jobs" } };
            var count = 0;
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                Assert.Equal(count++, msg.Data);
                if (count == 10)
                    break;
            }
        }
    }
}
