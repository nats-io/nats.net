using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;
using NATS.Client.Core;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using NATS.Net;

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
    public async Task Pinned_client_basic_flow_with_consume()
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

        // First consumer consumes - becomes pinned
        string? pinId = null;
        var count1 = 0;
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 5,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer1.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
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

                await msg.AckAsync(cancellationToken: cts.Token);
                if (count1 == 5)
                    break;
            }
        }

        Assert.Equal(5, count1);
        Assert.NotNull(pinId);
        _output.WriteLine($"First consumer got {count1} messages with pin ID: {pinId}");
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

        // First consumer consumes and becomes pinned
        string? firstPinId = null;
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 1,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer1.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                if (msg.Headers != null && msg.Headers.TryGetValue("Nats-Pin-Id", out var pinIdValues))
                {
                    firstPinId = pinIdValues.ToString();
                }

                await msg.AckAsync(cancellationToken: cts.Token);
                break;
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
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 3,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer2.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                count2++;
                if (msg.Headers != null && msg.Headers.TryGetValue("Nats-Pin-Id", out var pinIdValues))
                {
                    secondPinId = pinIdValues.ToString();
                }

                await msg.AckAsync(cancellationToken: cts.Token);
                if (count2 == 3)
                    break;
            }
        }

        Assert.True(count2 > 0, "Second consumer should receive messages after unpin");
        Assert.NotNull(secondPinId);
        Assert.NotEqual(firstPinId, secondPinId);
        _output.WriteLine($"Second consumer got {count2} messages with new pin ID: {secondPinId}");
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Pin_id_mismatch_should_trigger_immediate_repull(NatsRequestReplyMode mode)
    {
        const string pinId = "pin-1";
        var pullRequests = new ConcurrentQueue<string>();
        var mismatchInjected = 0;
        string? subscriptionSubject = null;
        string? subscriptionSid = null;

        static async Task SendHMsgAsync(MockServer.Client client, string subject, string sid, string? replyTo, string headers, string? payload)
        {
            if (!headers.EndsWith("\r\n", StringComparison.Ordinal))
            {
                headers += "\r\n";
            }

            headers += "\r\n";
            payload ??= string.Empty;

            var replyToPart = string.IsNullOrWhiteSpace(replyTo) ? string.Empty : $" {replyTo}";
            var hsize = headers.Length;
            var size = hsize + payload.Length;

            await client.Writer.WriteAsync($"HMSG {subject} {sid}{replyToPart} {hsize} {size}\r\n{headers}{payload}\r\n");
            await client.Writer.FlushAsync();
        }

        await using var ms = new MockServer(async (_, cmd) =>
        {
            if (cmd.Name == "SUB")
            {
                subscriptionSubject = cmd.Subject;
                subscriptionSid = cmd.Sid;
                return;
            }

            if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.INFO", StringComparison.Ordinal))
            {
                cmd.Reply(payload: """{"stream_name":"x","name":"x"}""");
                return;
            }

            if (cmd.Name != "PUB" || !cmd.Subject.Contains("CONSUMER.MSG.NEXT", StringComparison.Ordinal))
            {
                return;
            }

            var request = cmd.Buffer is null ? string.Empty : new string(cmd.Buffer);
            pullRequests.Enqueue(request);

            if (pullRequests.Count == 1)
            {
                Assert.NotNull(subscriptionSid);
                await SendHMsgAsync(
                    cmd.Client,
                    subject: "x.1",
                    sid: subscriptionSid!,
                    replyTo: null,
                    headers: $"NATS/1.0\r\nNats-Pending-Messages: 1\r\nNats-Pin-Id: {pinId}",
                    payload: "1");
                return;
            }

            if (pullRequests.Count == 2)
            {
                Assert.NotNull(subscriptionSubject);
                Assert.NotNull(subscriptionSid);
                Assert.Contains(pinId, request);

                await SendHMsgAsync(
                    cmd.Client,
                    subject: subscriptionSubject!,
                    sid: subscriptionSid!,
                    replyTo: null,
                    headers: "NATS/1.0 423 Pin ID Mismatch",
                    payload: null);
                Interlocked.Exchange(ref mismatchInjected, 1);
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url, RequestReplyMode = mode });
        var js = nats.CreateJetStreamContext();
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("x", "x", cts.Token);

        var consumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 1,
            ThresholdMsgs = 0,
            Expires = TimeSpan.FromSeconds(5),
            IdleHeartbeat = TimeSpan.FromSeconds(1),
        };

        await using var cc = await consumer.ConsumeInternalAsync<int>(opts: consumeOpts, cancellationToken: cts.Token);

        var first = await cc.Msgs.ReadAsync(cts.Token);
        Assert.NotNull(first.Headers);
        Assert.True(first.Headers.TryGetValue("Nats-Pin-Id", out var pinIdValues));
        Assert.Equal(pinId, pinIdValues.ToString());

        cc.Delivered(first.Size);

        await Retry.Until(
            "pin mismatch injected",
            () => Volatile.Read(ref mismatchInjected) == 1,
            timeout: TimeSpan.FromSeconds(5),
            retryDelay: TimeSpan.FromMilliseconds(25));

        await Retry.Until(
            "immediate repull after pin mismatch",
            () => pullRequests.Count >= 3,
            timeout: TimeSpan.FromMilliseconds(500),
            retryDelay: TimeSpan.FromMilliseconds(25));

        var requests = pullRequests.ToArray();
        Assert.DoesNotContain(pinId, requests[2]);
    }

    [Fact]
    public async Task Pinned_client_not_allowed_with_fetch()
    {
        var nats = new NatsConnection();
        var js = new NatsJSContext(nats);

        var consumer = new NatsJSConsumer(js, new ConsumerInfo
        {
            StreamName = "s1",
            Name = "c1",
            Config = new ConsumerConfig("c1")
            {
                PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            },
        });

        var exception = await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await foreach (var msg in consumer.FetchAsync<int>(new NatsJSFetchOpts { MaxMsgs = 1 }))
            {
            }
        });

        Assert.Contains("fetch", exception.Message);
        Assert.Contains("Pinned", exception.Message);
    }

    [Fact]
    public async Task Pinned_client_not_allowed_with_fetch_no_wait()
    {
        var nats = new NatsConnection();
        var js = new NatsJSContext(nats);

        var consumer = new NatsJSConsumer(js, new ConsumerInfo
        {
            StreamName = "s1",
            Name = "c1",
            Config = new ConsumerConfig("c1")
            {
                PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            },
        });

        var exception = await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await foreach (var msg in consumer.FetchNoWaitAsync<int>(new NatsJSFetchOpts { MaxMsgs = 1 }))
            {
            }
        });

        Assert.Contains("fetch", exception.Message);
        Assert.Contains("Pinned", exception.Message);
    }

    [Fact]
    public async Task Pinned_client_not_allowed_with_next()
    {
        var nats = new NatsConnection();
        var js = new NatsJSContext(nats);

        var consumer = new NatsJSConsumer(js, new ConsumerInfo
        {
            StreamName = "s1",
            Name = "c1",
            Config = new ConsumerConfig("c1")
            {
                PriorityPolicy = ConsumerConfigPriorityPolicy.PinnedClient,
            },
        });

        var exception = await Assert.ThrowsAsync<NatsJSException>(async () =>
        {
            await consumer.NextAsync<int>();
        });

        Assert.Contains("next", exception.Message);
        Assert.Contains("Pinned", exception.Message);
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

        // Consume with invalid priority group should fail
        var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 5,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "invalid_group" },
            };
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
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
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 1,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer1.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                break;
            }
        }

        // Unpin via context method
        await js.UnpinConsumerAsync($"{prefix}s1", $"{prefix}c1", "workers", cts.Token);

        // Another consumer can now consume
        var consumer2 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
        var count2 = 0;
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 3,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer2.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                count2++;
                await msg.AckAsync(cancellationToken: cts.Token);
                if (count2 == 3)
                    break;
            }
        }

        Assert.True(count2 > 0, "Consumer should receive messages after context unpin");
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

        // Consume to get pinned
        {
            var opts = new NatsJSConsumeOpts
            {
                MaxMsgs = 1,
                PriorityGroup = new NatsJSPriorityGroupOpts { Group = "workers" },
            };
            await foreach (var msg in consumer.ConsumeAsync<int>(opts: opts, cancellationToken: cts.Token))
            {
                await msg.AckAsync(cancellationToken: cts.Token);
                break;
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

public class PinnedClientMockServerTest
{
    [Fact]
    public async Task Pin_id_from_headers_should_use_last_value_when_multiple_headers_present()
    {
        await using var ms = new MockServer((_, cmd) =>
        {
            if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.INFO", StringComparison.Ordinal))
            {
                cmd.Reply(payload: """{"stream_name":"x","name":"x"}""");
            }

            return Task.CompletedTask;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        var js = nats.CreateJetStreamContext();
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("x", "x", cts.Token);
        var headers = new NatsHeaders
        {
            { "Nats-Pin-Id", new StringValues(["pin-stale", "pin-current"]) },
        };

        NatsJSExtensionsInternal.TrySetPinIdFromHeaders(headers, consumer);

        Assert.Equal("pin-current", consumer.GetPinId());
    }

    [Fact]
    public async Task Queued_consume_pull_request_should_use_latest_pin_id_when_sent()
    {
        var pullRequestCount = 0;
        var secondPullRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var ms = new MockServer((_, cmd) =>
        {
            if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.INFO", StringComparison.Ordinal))
            {
                cmd.Reply(payload: """{"stream_name":"x","name":"x"}""");
                return Task.CompletedTask;
            }

            if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.MSG.NEXT", StringComparison.Ordinal))
            {
                var request = cmd.Buffer is null ? string.Empty : new string(cmd.Buffer);
                if (Interlocked.Increment(ref pullRequestCount) == 2)
                {
                    secondPullRequest.TrySetResult(request);
                }
            }

            return Task.CompletedTask;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        var js = nats.CreateJetStreamContext();
        var consumer = (NatsJSConsumer)await js.GetConsumerAsync("x", "x", cts.Token);

        consumer.SetPinId("pin-old");

        await using var cc = await consumer.ConsumeInternalAsync<int>(
            opts: new NatsJSConsumeOpts { MaxMsgs = 1, ThresholdMsgs = 0 },
            cancellationToken: cts.Token);

        cc.Delivered(1);
        consumer.SetPinId("pin-new");

        await secondPullRequest.Task.WaitAsync(cts.Token);
        var request = await secondPullRequest.Task;
        Assert.Contains(@"""id"":""pin-new""", request);
        Assert.DoesNotContain(@"""id"":""pin-old""", request);
    }
}
