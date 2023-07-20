using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Client.Core.Tests;

public class JetStreamTest
{
    private readonly ITestOutputHelper _output;

    public JetStreamTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_stream_test()
    {
        await using var server = new NatsServer(new NullOutputHelper(), new NatsServerOptionsBuilder().UseTransport(TransportType.Tcp).UseJetStream().Build());
        var nats = server.CreateClientConnection();

        // Happy user
        {
            var context = new JSContext(nats, new JSOptions());

            // Create stream
            var response = await context.CreateStreamAsync(request: new StreamConfiguration
            {
                Name = "events", Subjects = new[] { "events.*" },
            });
            Assert.True(response.Success);
            Assert.Null(response.Error);
            Assert.NotNull(response.Response);
            Assert.Equal("events", response.Response.Config.Name);

            // Create consumer
            var consumerInfo = await context.CreateConsumerAsync(new ConsumerCreateRequest
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
            Assert.True(consumerInfo.Success);
            Assert.Null(consumerInfo.Error);
            Assert.NotNull(consumerInfo.Response);
            Assert.Equal("events", consumerInfo.Response.StreamName);
            Assert.Equal("consumer1", consumerInfo.Response.Config.Name);

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
            var messages = new List<NatsMsg>();
            await foreach (var msg in context.ConsumeAsync(
                               stream: "events",
                               consumer: "consumer1",
                               request: new ConsumerGetnextRequest { Batch = 100 },
                               requestOpts: new NatsSubOpts { CanBeCancelled = true },
                               cancellationToken: cts.Token))
            {
                messages.Add(msg);

                // Only ACK one message so we can consume again
                if (messages.Count == 1)
                {
                    await msg.ReplyAsync(new ReadOnlySequence<byte>("+ACK"u8.ToArray()), cancellationToken: cts.Token);
                }

                if (messages.Count == 2)
                {
                    break;
                }
            }

            Assert.Equal(2, messages.Count);
            Assert.Equal("events.foo", messages[0].Subject);
            Assert.Equal("events.foo", messages[1].Subject);

            // Consume the unacknowledged message
            await foreach (var msg in context.ConsumeAsync(
                               stream: "events",
                               consumer: "consumer1",
                               request: new ConsumerGetnextRequest { Batch = 100 },
                               requestOpts: new NatsSubOpts { CanBeCancelled = true },
                               cancellationToken: cts.Token))
            {
                Assert.Equal("events.foo", msg.Subject);
                break;
            }
        }

        // Handle errors
        {
            var context = new JSContext(nats, new JSOptions());

            var response = await context.CreateStreamAsync(request: new StreamConfiguration
            {
                Name = "events2", Subjects = new[] { "events.*" },
            });
            Assert.False(response.Success);
            Assert.Null(response.Response);
            Assert.NotNull(response.Error);
            Assert.Equal(400, response.Error.Code);

            // subjects overlap with an existing stream
            Assert.Equal(10065, response.Error.ErrCode);
        }

        // Delete stream
        {
            var context = new JSContext(nats, new JSOptions());
            JSResponse<StreamMsgDeleteResponse> response;

            // Success
            response = await context.DeleteStreamAsync("events");
            Assert.True(response.Success);
            Assert.True(response.Response!.Success);

            // Error
            response = await context.DeleteStreamAsync("events2");
            Assert.False(response.Success);
            Assert.Equal(404, response.Error!.Code);

            // stream not found
            Assert.Equal(10059, response.Error!.ErrCode);
        }
    }
}

public class TestData
{
    public int Test { get; set; }
}
