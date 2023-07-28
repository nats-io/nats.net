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
        var context = new NatsJSContext(nats, new NatsJSOptions());
        var streams = new NatsJSManageStreams(context);
        var consumers = new NatsJSManageConsumers(context);
        await streams.CreateAsync("s1", "s1.*");

        // Create
        {
            var consumerInfo = await consumers.CreateAsync(new ConsumerCreateRequest
            {
                StreamName = "s1", Config = new ConsumerConfiguration { Name = "c1", DurableName = "c1" },
            });
            Assert.Equal("s1", consumerInfo.StreamName);
            Assert.Equal("c1", consumerInfo.Config.Name);
        }

        // Get
        {
            var consumerInfo = await consumers.GetAsync("s1", "c1");
            Assert.Equal("s1", consumerInfo.StreamName);
            Assert.Equal("c1", consumerInfo.Config.Name);
        }
    }

    [Fact]
    public async Task List_delete_consumer()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var context = new NatsJSContext(nats, new NatsJSOptions());
        var streams = new NatsJSManageStreams(context);
        var consumers = new NatsJSManageConsumers(context);
        await streams.CreateAsync("s1", "s1.*");
        await consumers.CreateAsync("s1", "c1");
        await consumers.CreateAsync("s1", "c2");
        await consumers.CreateAsync("s1", "c3");

        // List
        {
            var consumerList = await consumers.ListAsync("s1", new ConsumerListRequest());
            var list = consumerList.Consumers.ToList();
            Assert.Equal(3, list.Count);
            Assert.True(list.All(c => c.StreamName == "s1"));
            Assert.True(list.Any(c => c.Config.Name == "c1"));
            Assert.True(list.Any(c => c.Config.Name == "c2"));
            Assert.True(list.Any(c => c.Config.Name == "c3"));
        }

        // Delete
        {
            var response = await consumers.DeleteAsync("s1", "c1");
            Assert.True(response.Success);

            var consumerList = await consumers.ListAsync("s1", new ConsumerListRequest());
            var list = consumerList.Consumers.ToList();
            Assert.Equal(2, list.Count);
            Assert.False(list.Any(c => c.Config.Name == "c1"));
        }
    }
}
