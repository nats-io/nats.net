using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PinnedClientTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public PinnedClientTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Pinned_client_basic_flow_with_fetch()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Create stream
        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        // Publish messages
        for (var i = 0; i < 10; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.{i}", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        // Create consumer with PinnedClient policy
        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            PinnedTTL = TimeSpan.FromSeconds(30),
        };
        var consumer1 = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // First consumer fetches - becomes pinned
        string? pinId = null;
        var count1 = 0;
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 5,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer1.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                count1++;

                // Verify Nats-Pin-Id header is present
                Assert.NotNull(msg.Headers);
                Assert.True(msg.Headers.TryGetValue("Nats-Pin-Id", out var pinIdValues));
                var currentPinId = pinIdValues.ToString();
                Assert.False(string.IsNullOrEmpty(currentPinId));

                // All messages should have the same pin ID
                if (pinId == null)
                {
                    pinId = currentPinId;
                }
                else
                {
                    Assert.Equal(pinId, currentPinId);
                }
            }
        }

        Assert.Equal(5, count1);
        Assert.NotNull(pinId);
        _output.WriteLine($"First consumer got {count1} messages with pin ID: {pinId}");

        // Second consumer context (same consumer name) tries to fetch - should get no messages
        var consumer2 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var count2 = 0;
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 5,
                Expires = TimeSpan.FromSeconds(2),
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer2.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                count2++;
            }
        }

        // Second consumer should get no messages while first is pinned
        Assert.Equal(0, count2);
        _output.WriteLine($"Second consumer got {count2} messages (expected 0 while pinned)");
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Unpin_allows_other_consumer_to_receive()
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

        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            PinnedTTL = TimeSpan.FromSeconds(60),
        };
        var consumer1 = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // First consumer fetches and becomes pinned
        string? firstPinId = null;
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 3,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer1.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                if (msg.Headers != null && msg.Headers.TryGetValue("Nats-Pin-Id", out var pinIdValues))
                {
                    firstPinId = pinIdValues.ToString();
                }
            }
        }

        Assert.NotNull(firstPinId);
        _output.WriteLine($"First pin ID: {firstPinId}");

        // Unpin the consumer
        await consumer1.UnpinAsync("workers", cts.Token);
        _output.WriteLine("Consumer unpinned");

        // Now second consumer should be able to get messages with a new pin ID
        var consumer2 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        string? secondPinId = null;
        var count2 = 0;
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 3,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer2.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                count2++;
                if (msg.Headers != null && msg.Headers.TryGetValue("Nats-Pin-Id", out var pinIdValues))
                {
                    secondPinId = pinIdValues.ToString();
                }
            }
        }

        Assert.True(count2 > 0, "Second consumer should receive messages after unpin");
        Assert.NotNull(secondPinId);
        Assert.NotEqual(firstPinId, secondPinId);
        _output.WriteLine($"Second consumer got {count2} messages with new pin ID: {secondPinId}");
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Pinned_client_requires_priority_group_on_fetch()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
        };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Fetch without priority group should fail
        var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
        {
            var opts = new NatsJSFetchOpts { MaxMsgs = 5 };
            await foreach (var msg in consumer.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
            }
        });

        Assert.Equal(400, exception.HeaderCode);
        Assert.Contains("Priority Group", exception.HeaderMessageText, StringComparison.OrdinalIgnoreCase);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Pinned_client_requires_priority_group_on_next()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        await js.PublishAsync($"{prefix}s1.1", 1, cancellationToken: cts.Token);

        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
        };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Next without priority group should fail
        var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
        {
            await consumer.NextAsync<int>(cancellationToken: cts.Token);
        });

        Assert.Equal(400, exception.HeaderCode);
        Assert.Contains("Priority Group", exception.HeaderMessageText, StringComparison.OrdinalIgnoreCase);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Invalid_priority_group_returns_error()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
        };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Fetch with invalid priority group should fail
        var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 5,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "invalid_group" },
            };
            await foreach (var msg in consumer.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
            }
        });

        Assert.Equal(400, exception.HeaderCode);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Context_unpin_consumer_async()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        for (var i = 0; i < 5; i++)
        {
            var ack = await js.PublishAsync($"{prefix}s1.{i}", i, cancellationToken: cts.Token);
            ack.EnsureSuccess();
        }

        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            PinnedTTL = TimeSpan.FromSeconds(60),
        };
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // First consumer gets pinned
        var consumer1 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 2,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer1.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                // Get pinned
            }
        }

        // Unpin via context method
        await js.UnpinConsumerAsync($"{prefix}s1", $"{prefix}c1", "workers", cts.Token);

        // Another consumer can now fetch
        var consumer2 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var count = 0;
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 3,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer2.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                count++;
            }
        }

        Assert.True(count > 0, "Consumer should receive messages after context unpin");
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Consumer_info_shows_priority_groups_state()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var js = new NatsJSContext(nats);
        var prefix = _server.GetNextId();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.>"], cts.Token);

        await js.PublishAsync($"{prefix}s1.1", 1, cancellationToken: cts.Token);

        var consumerConfig = new ConsumerConfig($"{prefix}c1")
        {
            PriorityGroups = ["workers"],
            PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            PinnedTTL = TimeSpan.FromSeconds(60),
        };
        var consumer = await js.CreateOrUpdateConsumerAsync($"{prefix}s1", consumerConfig, cancellationToken: cts.Token);

        // Verify config
        Assert.Equal(ConsumerConfigPriorityPolicy.PinnedClient, consumer.Info.Config.PriorityPolicy);
        Assert.NotNull(consumer.Info.Config.PriorityGroups);
        Assert.Contains("workers", consumer.Info.Config.PriorityGroups);

        // Fetch to get pinned
        {
            var opts = new NatsJSFetchOpts
            {
                MaxMsgs = 1,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer.FetchAsync<int>(opts, cancellationToken: cts.Token))
            {
                // Gets pinned
            }
        }

        // Refresh and check priority groups state
        await consumer.RefreshAsync(cts.Token);

        Assert.NotNull(consumer.Info.PriorityGroups);
        var workerGroup = consumer.Info.PriorityGroups.FirstOrDefault(g => g.Group == "workers");
        Assert.NotNull(workerGroup);
        Assert.NotNull(workerGroup.PinnedClientId);
        _output.WriteLine($"Pinned client ID in state: {workerGroup.PinnedClientId}");
    }
}
