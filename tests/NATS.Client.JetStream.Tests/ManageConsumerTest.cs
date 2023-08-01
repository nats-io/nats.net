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
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats, new NatsJSOptions());
        await js.CreateStreamAsync("s1", "s1.*");

        // Create
        {
            var consumer = await js.CreateConsumerAsync(new ConsumerCreateRequest
            {
                StreamName = "s1",
                Config = new ConsumerConfiguration { Name = "c1", DurableName = "c1" },
            });
            Assert.Equal("s1", consumer.Info.StreamName);
            Assert.Equal("c1", consumer.Info.Config.Name);
        }

        // Get
        {
            var consumer = await js.GetConsumerAsync("s1", "c1");
            Assert.Equal("s1", consumer.Info.StreamName);
            Assert.Equal("c1", consumer.Info.Config.Name);
        }
    }

    [Fact]
    public async Task List_delete_consumer()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats, new NatsJSOptions());
        await js.CreateStreamAsync("s1", "s1.*");
        await js.CreateConsumerAsync("s1", "c1");
        await js.CreateConsumerAsync("s1", "c2");
        await js.CreateConsumerAsync("s1", "c3");

        // List
        {
            var list = new List<NatsJSConsumer>();
            await foreach (var consumer in js.ListConsumersAsync("s1", new ConsumerListRequest()))
            {
                list.Add(consumer);
            }

            Assert.Equal(3, list.Count);
            Assert.True(list.All(c => c.Info.StreamName == "s1"));
            Assert.Contains(list, c => c.Info.Config.Name == "c1");
            Assert.Contains(list, c => c.Info.Config.Name == "c2");
            Assert.Contains(list, c => c.Info.Config.Name == "c3");
        }

        // Delete
        {
            var response = await js.DeleteConsumerAsync("s1", "c1");
            Assert.True(response);

            var list = new List<NatsJSConsumer>();
            await foreach (var consumer in js.ListConsumersAsync("s1", new ConsumerListRequest()))
            {
                list.Add(consumer);
            }

            Assert.Equal(2, list.Count);
            Assert.DoesNotContain(list, c => c.Info.Config.Name == "c1");
        }
    }
}
