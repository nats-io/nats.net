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
            var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var js = new NatsJSContext(nats, new NatsJSOpts());

            // Create stream
            var stream = await js.CreateStreamAsync(
                request: new StreamConfiguration { Name = "events", Subjects = new[] { "events.*" }, },
                cancellationToken: cts1.Token);
            Assert.Equal("events", stream.Info.Config.Name);

            // Create consumer
            var consumer = await js.CreateConsumerAsync(
                new ConsumerCreateRequest
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
                },
                cts1.Token);
            Assert.Equal("events", consumer.Info.StreamName);
            Assert.Equal("consumer1", consumer.Info.Config.Name);

            // Publish
            var ack = await js.PublishAsync("events.foo", new TestData { Test = 1 }, cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(1, ack.Seq);
            Assert.False(ack.Duplicate);

            // Message ID
            ack = await js.PublishAsync(
                "events.foo",
                new TestData { Test = 2 },
                new NatsPubOpts { Headers = new NatsHeaders { { "Nats-Msg-Id", "test2" } }, },
                cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(2, ack.Seq);
            Assert.False(ack.Duplicate);

            // Duplicate
            ack = await js.PublishAsync(
                "events.foo",
                new TestData { Test = 2 },
                new NatsPubOpts { Headers = new NatsHeaders { { "Nats-Msg-Id", "test2" } }, },
                cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(2, ack.Seq);
            Assert.True(ack.Duplicate);

            // Consume
            var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var messages = new List<NatsJSMsg<TestData?>>();
            await foreach (var msg in consumer.ConsumeAsync<TestData>(
                               new NatsJSConsumeOpts { MaxMsgs = 100 },
                               cancellationToken: cts2.Token))
            {
                messages.Add(msg);

                // Only ACK one message so we can consume again
                if (messages.Count == 1)
                {
                    await msg.Ack(cts2.Token);
                }

                if (messages.Count == 2)
                {
                    break;
                }
            }

            Assert.Equal(2, messages.Count);
            Assert.Equal("events.foo", messages[0].Msg.Subject);
            Assert.Equal("events.foo", messages[1].Msg.Subject);

            // Consume the unacknowledged message
            await foreach (var msg in consumer.ConsumeAsync<TestData>(
                               new NatsJSConsumeOpts { MaxMsgs = 100 },
                               cancellationToken: cts2.Token))
            {
                Assert.Equal("events.foo", msg.Msg.Subject);
                break;
            }
        }

        // Handle errors
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var js = new NatsJSContext(nats, new NatsJSOpts());
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            {
                await js.CreateStreamAsync(
                    request: new StreamConfiguration
                    {
                        Name = "events2",
                        Subjects = new[] { "events.*" },
                    },
                    cancellationToken: cts.Token);
            });
            Assert.Equal(400, exception.Error.Code);

            // subjects overlap with an existing stream
            Assert.Equal(10065, exception.Error.ErrCode);
        }

        // Delete stream
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var js = new NatsJSContext(nats, new NatsJSOpts());

            // Success
            await js.DeleteStreamAsync("events", cts.Token);

            // Error
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            {
                await js.DeleteStreamAsync("events2", cts.Token);
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
