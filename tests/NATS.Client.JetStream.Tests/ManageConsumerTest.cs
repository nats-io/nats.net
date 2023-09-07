using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ManageConsumerTest
{
    private readonly ITestOutputHelper _output;

    public ManageConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_get_consumer()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        // Create
        {
            var consumer = await js.CreateConsumerAsync(
                new ConsumerCreateRequest
                {
                    StreamName = "s1",
                    Config = new ConsumerConfiguration
                    {
                        Name = "c1",
                        DurableName = "c1",
                        AckPolicy = ConsumerConfigurationAckPolicy.@explicit,
                    },
                },
                cts.Token);
            Assert.Equal("s1", consumer.Info.StreamName);
            Assert.Equal("c1", consumer.Info.Config.Name);
        }

        // Get
        {
            var consumer = await js.GetConsumerAsync("s1", "c1", cts.Token);
            Assert.Equal("s1", consumer.Info.StreamName);
            Assert.Equal("c1", consumer.Info.Config.Name);
        }
    }

    [Fact]
    public async Task List_delete_consumer()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateConsumerAsync("s1", "c1", cancellationToken: cts.Token);
        await js.CreateConsumerAsync("s1", "c2", cancellationToken: cts.Token);
        await js.CreateConsumerAsync("s1", "c3", cancellationToken: cts.Token);

        // List
        {
            var list = new List<ConsumerInfo>();
            await foreach (var consumer in js.ListConsumersAsync("s1", cts.Token))
            {
                list.Add(consumer.Info);
            }

            Assert.Equal(3, list.Count);
            Assert.True(list.All(c => c.StreamName == "s1"));
            Assert.Contains(list, c => c.Config.Name == "c1");
            Assert.Contains(list, c => c.Config.Name == "c2");
            Assert.Contains(list, c => c.Config.Name == "c3");
        }

        // Delete
        {
            var response = await js.DeleteConsumerAsync("s1", "c1", cts.Token);
            Assert.True(response);

            var list = new List<ConsumerInfo>();
            await foreach (var consumer in js.ListConsumersAsync("s1", cts.Token))
            {
                list.Add(consumer.Info);
            }

            Assert.Equal(2, list.Count);
            Assert.DoesNotContain(list, c => c.Config.Name == "c1");
        }
    }
}
