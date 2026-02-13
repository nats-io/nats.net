using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class JetStreamTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public JetStreamTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create_stream_test(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestTimeout = TimeSpan.FromSeconds(5), RequestReplyMode = mode });
        var prefix = _server.GetNextId() + "-";
        _output.WriteLine($"prefix: {prefix}");

        await nats.ConnectRetryAsync();

        // Happy user
        {
            var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var js = new NatsJSContext(nats);

            // Create stream
            var stream = await js.CreateStreamAsync(
                config: new StreamConfig { Name = $"{prefix}events", Subjects = new[] { $"{prefix}events.*" }, },
                cancellationToken: cts1.Token);
            Assert.Equal($"{prefix}events", stream.Info.Config.Name);

            // Create consumer
            var consumer = (NatsJSConsumer)await js.CreateOrUpdateConsumerAsync(
                $"{prefix}events",
                new ConsumerConfig
                {
                    Name = $"{prefix}consumer1",
                    DurableName = $"{prefix}consumer1",
                },
                cts1.Token);
            Assert.Equal($"{prefix}events", consumer.Info.StreamName);
            Assert.Equal($"{prefix}consumer1", consumer.Info.Config.Name);

            // Publish
            var ack = await js.PublishAsync($"{prefix}events.foo", new TestData { Test = 1 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal($"{prefix}events", ack.Stream);
            Assert.Equal(1, (int)ack.Seq);
            Assert.False(ack.Duplicate);

            // Message ID
            ack = await js.PublishAsync(
                $"{prefix}events.foo",
                new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "test2" },
                cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal($"{prefix}events", ack.Stream);
            Assert.Equal(2, (int)ack.Seq);
            Assert.False(ack.Duplicate);

            // Duplicate
            ack = await js.PublishAsync(
                $"{prefix}events.foo",
                new TestData { Test = 2 },
                serializer: TestDataJsonSerializer<TestData>.Default,
                opts: new NatsJSPubOpts { MsgId = "test2" },
                cancellationToken: cts1.Token);
            Assert.Null(ack.Error);
            Assert.Equal($"{prefix}events", ack.Stream);
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
            Assert.Equal($"{prefix}events.foo", messages[0].Subject);
            Assert.Equal($"{prefix}events.foo", messages[1].Subject);
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
                        Name = $"{prefix}events2",
                        Subjects = new[] { $"{prefix}events.*" },
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
            await js.DeleteStreamAsync($"{prefix}events", cts.Token);

            // Error
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            {
                await js.DeleteStreamAsync($"{prefix}events2", cts.Token);
            });

            Assert.Equal(404, exception.Error.Code);

            // stream not found
            Assert.Equal(10059, exception.Error.ErrCode);
        }
    }
}
