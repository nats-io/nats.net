using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ManageStreamTest
{
    private readonly ITestOutputHelper _output;

    public ManageStreamTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Account_info_create_get_update_stream()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var context = new NatsJSContext(nats, new NatsJSOptions());
        var streams = new NatsJSManageStreams(context);

        // Account Info
        {
            var accountInfo = await context.GetAccountInfoAsync();
            Assert.Equal(0, accountInfo.Streams);
        }

        // Create
        {
            var info = await streams.CreateAsync(request: new StreamConfiguration
            {
                Name = "events",
                Subjects = new[] { "events.*" },
            });
            Assert.Equal("events", info.Config.Name);

            var accountInfo = await context.GetAccountInfoAsync();
            Assert.Equal(1, accountInfo.Streams);
        }

        // Get
        {
            var info = await streams.GetAsync("events");
            Assert.Equal("events", info.Config.Name);
            Assert.Equal(new[] { "events.*" }, info.Config.Subjects);
        }

        // Update
        {
            var info = await streams.GetAsync("events");
            Assert.Equal(-1, info.Config.MaxMsgs);

            var response = await streams.UpdateAsync(new StreamUpdateRequest { Name = "events", MaxMsgs = 10 });
            Assert.Equal(10, response.Config.MaxMsgs);

            var info2 = await streams.GetAsync("events");
            Assert.Equal(10, info2.Config.MaxMsgs);
        }
    }

    [Fact]
    public async Task List_delete_stream()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var context = new NatsJSContext(nats, new NatsJSOptions());
        var streams = new NatsJSManageStreams(context);

        await streams.CreateAsync("s1", "s1.*");
        await streams.CreateAsync("s2", "s2.*");
        await streams.CreateAsync("s3", "s3.*");

        // List
        {
            var response = await streams.ListAsync(new StreamListRequest());
            var list = response.Streams.ToList();
            Assert.Equal(3, list.Count);
            Assert.Contains(list, s => s.Config.Name == "s1");
            Assert.Contains(list, s => s.Config.Name == "s2");
            Assert.Contains(list, s => s.Config.Name == "s3");
        }

        // Delete
        {
            var deleteResponse = await streams.DeleteAsync("s1");
            Assert.True(deleteResponse.Success);

            var response = await streams.ListAsync(new StreamListRequest());
            var list = response.Streams.ToList();
            Assert.DoesNotContain(list, s => s.Config.Name == "s1");
        }
    }
}
