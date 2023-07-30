using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class JetStreamTest
{
    private readonly ITestOutputHelper _output;

    public JetStreamTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_stream_test()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();

        // Happy user
        {
            var context = new NatsJSContext(nats, new NatsJSOptions());
            var streams = new NatsJSManageStreams(context);
            var consumers = new NatsJSManageConsumers(context);

            // Create stream
            var info = await streams.CreateAsync(request: new StreamConfiguration
            {
                Name = "events",
                Subjects = new[] { "events.*" },
            });
            Assert.Equal("events", info.Config.Name);

            // Create consumer
            var consumerInfo = await consumers.CreateAsync(new ConsumerCreateRequest
            {
                StreamName = "events",
                Config = new ConsumerConfiguration
                {
                    Name = "consumer1",
                    DurableName = "consumer1",

                    // Turn on ACK so we can test them below
                    AckPolicy = ConsumerConfigurationAckPolicy.@explicit,

                    // Effectively set message expiry for the consumer
                    // so that unacknowledged messages can be put back into
                    // the consumer to be delivered again (in a sense).
                    // This is to make below consumer tests work.
                    AckWait = 2_000_000_000, // 2 seconds
                },
            });
            Assert.Equal("events", consumerInfo.StreamName);
            Assert.Equal("consumer1", consumerInfo.Config.Name);

            // Publish
            PubAckResponse ack;
            ack = await context.PublishAsync("events.foo", new TestData { Test = 1 });
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(1, ack.Seq);
            Assert.False(ack.Duplicate);

            // Message ID
            ack = await context.PublishAsync("events.foo", new TestData { Test = 2 }, new NatsPubOpts
            {
                Headers = new NatsHeaders { { "Nats-Msg-Id", "test2" } },
            });
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(2, ack.Seq);
            Assert.False(ack.Duplicate);

            // Duplicate
            ack = await context.PublishAsync("events.foo", new TestData { Test = 2 }, new NatsPubOpts
            {
                Headers = new NatsHeaders { { "Nats-Msg-Id", "test2" } },
            });
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(2, ack.Seq);
            Assert.True(ack.Duplicate);

            // Consume
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var messages = new List<NatsJSControlMsg>();
            var consumer = new NatsJSConsumer(context, new ConsumerInfo { Name = "consumer1", StreamName = "events" }, new ConsumerOpts());
            await foreach (var msg in consumer.ConsumeRawAsync(
                               request: new ConsumerGetnextRequest { Batch = 100 },
                               requestOpts: default,
                               cancellationToken: cts.Token))
            {
                messages.Add(msg);

                // Only ACK one message so we can consume again
                if (messages.Count == 1)
                {
                    await msg.JSMsg!.Value.Ack(cts.Token);
                }

                if (messages.Count == 2)
                {
                    break;
                }
            }

            Assert.Equal(2, messages.Count);
            Assert.Equal("events.foo", messages[0].JSMsg!.Value.Msg.Subject);
            Assert.Equal("events.foo", messages[1].JSMsg!.Value.Msg.Subject);

            // Consume the unacknowledged message
            await foreach (var msg in consumer.ConsumeRawAsync(
                               request: new ConsumerGetnextRequest { Batch = 100 },
                               requestOpts: default,
                               cancellationToken: cts.Token))
            {
                Assert.Equal("events.foo", msg.JSMsg!.Value.Msg.Subject);
                break;
            }
        }

        // Handle errors
        {
            var context = new NatsJSContext(nats, new NatsJSOptions());
            var streams = new NatsJSManageStreams(context);

            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            {
                await streams.CreateAsync(request: new StreamConfiguration
                {
                    Name = "events2",
                    Subjects = new[] { "events.*" },
                });
            });
            Assert.Equal(400, exception.Error.Code);

            // subjects overlap with an existing stream
            Assert.Equal(10065, exception.Error.ErrCode);
        }

        // Delete stream
        {
            var context = new NatsJSContext(nats, new NatsJSOptions());
            var streams = new NatsJSManageStreams(context);

            // Success
            await streams.DeleteAsync("events");

            // Error
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            {
                await streams.DeleteAsync("events2");
            });

            Assert.Equal(404, exception.Error.Code);

            // stream not found
            Assert.Equal(10059, exception.Error.ErrCode);
        }
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
