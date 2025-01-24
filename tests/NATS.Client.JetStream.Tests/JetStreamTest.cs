using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class JetStreamTest
{
    private readonly ITestOutputHelper _output;

    public JetStreamTest(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("Invalid.DotName")]
    [InlineData("Invalid SpaceName")]
    [InlineData(null)]
    public async Task Stream_invalid_name_test(string? streamName)
    {
        var jsmContext = new NatsJSContext(new NatsConnection());

        var cfg = new StreamConfig()
        {
            Name = streamName,
            Subjects = new[] { "events.*" },
        };

        // Create stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.CreateStreamAsync(cfg));

        // Create or update stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.CreateOrUpdateStreamAsync(cfg));

        // Delete stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.DeleteStreamAsync(streamName!));

        // Get stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.GetStreamAsync(streamName!, null));

        // Update stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.UpdateStreamAsync(cfg));

        // Purge stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.PurgeStreamAsync(streamName!, new StreamPurgeRequest()));

        // Get stream
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.GetStreamAsync(streamName!));

        // Delete Messages
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await jsmContext.DeleteMessageAsync(streamName!, new StreamMsgDeleteRequest()));
    }

    [Fact]
    public async Task Create_stream_test()
    {
        await using var server = await NatsServer.StartAsync(
            outputHelper: _output,
            opts: new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tcp)
                .Trace()
                .UseJetStream()
                .Build());
        var nats = await server.CreateClientConnectionAsync(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });

        // Happy user
        {
            var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var js = new NatsJSContext(nats);

            // Create stream
            var stream = await js.CreateStreamAsync(
                config: new StreamConfig { Name = "events", Subjects = new[] { "events.*" }, },
                cancellationToken: cts1.Token);
            Assert.Equal("events", stream.Info.Config.Name);

            // Create consumer
            var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync(
                "events",
                new ConsumerConfig
                {
                    Name = "consumer1",
                    DurableName = "consumer1",
                },
                cts1.Token);
            Assert.Equal("events", consumer.Info.StreamName);
            Assert.Equal("consumer1", consumer.Info.Config.Name);

            // Publish
            var ack = await js.PublishAsync("events.foo", new TestData { Test = 1 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(1, (int)ack.Seq);
            Assert.False(ack.Duplicate);

            // Message ID
            ack = await js.PublishAsync(
                "events.foo",
                new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "test2" },
                cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(2, (int)ack.Seq);
            Assert.False(ack.Duplicate);

            // Duplicate
            ack = await js.PublishAsync(
                "events.foo",
                new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "test2" },
                cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal("events", ack.Stream);
            Assert.Equal(2, (int)ack.Seq);
            Assert.True(ack.Duplicate);

            // Consume
            var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var messages = new List<NatsJSMsg<TestData>>();
            var cc = await consumer.ConsumeInternalAsync<TestData>(
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSConsumeOpts { MaxMsgs = 100 },
                cancellationToken: cts2.Token);
            await foreach (var msg in cc.Msgs.ReadAllAsync(cts2.Token))
            {
                messages.Add(msg);

                // Only ACK one message so we can consume again
                if (messages.Count == 1)
                {
                    await msg.AckAsync(cancellationToken: cts2.Token);
                }

                if (messages.Count == 2)
                {
                    break;
                }
            }

            Assert.Equal(2, messages.Count);
            Assert.Equal("events.foo", messages[0].Subject);
            Assert.Equal("events.foo", messages[1].Subject);
        }

        // Handle errors
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var js = new NatsJSContext(nats);
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            {
                await js.CreateStreamAsync(
                    config: new StreamConfig
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
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var js = new NatsJSContext(nats);

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
}
