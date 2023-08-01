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
        var js = new NatsJSContext(nats, new NatsJSOptions());

        // Account Info
        {
            var accountInfo = await js.GetAccountInfoAsync();
            Assert.Equal(0, accountInfo.Streams);
        }

        // Create
        {
            var stream = await js.CreateStreamAsync(request: new StreamConfiguration
            {
                Name = "events",
                Subjects = new[] { "events.*" },
            });
            Assert.Equal("events", stream.Info.Config.Name);

            var accountInfo = await js.GetAccountInfoAsync();
            Assert.Equal(1, accountInfo.Streams);
        }

        // Get
        {
            var stream = await js.GetStreamAsync("events");
            Assert.Equal("events", stream.Info.Config.Name);
            Assert.Equal(new[] { "events.*" }, stream.Info.Config.Subjects);
        }

        // Update
        {
            var stream1 = await js.GetStreamAsync("events");
            Assert.Equal(-1, stream1.Info.Config.MaxMsgs);

            var stream2 = await js.UpdateStreamAsync(new StreamUpdateRequest { Name = "events", MaxMsgs = 10 });
            Assert.Equal(10, stream2.Info.Config.MaxMsgs);

            var stream3 = await js.GetStreamAsync("events");
            Assert.Equal(10, stream3.Info.Config.MaxMsgs);
        }
    }

    [Fact]
    public async Task List_delete_stream()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats, new NatsJSOptions());

        await js.CreateStreamAsync("s1", "s1.*");
        await js.CreateStreamAsync("s2", "s2.*");
        await js.CreateStreamAsync("s3", "s3.*");

        // List
        {
            var list = new List<NatsJSStream>();
            await foreach (var stream in js.ListStreamsAsync(new StreamListRequest()))
            {
                list.Add(stream);
            }

            Assert.Equal(3, list.Count);
            Assert.Contains(list, s => s.Info.Config.Name == "s1");
            Assert.Contains(list, s => s.Info.Config.Name == "s2");
            Assert.Contains(list, s => s.Info.Config.Name == "s3");
        }

        // Delete
        {
            var deleteResponse = await js.DeleteStreamAsync("s1");
            Assert.True(deleteResponse);

            var list = new List<NatsJSStream>();
            await foreach (var stream in js.ListStreamsAsync(new StreamListRequest()))
            {
                list.Add(stream);
            }

            Assert.DoesNotContain(list, s => s.Info.Config.Name == "s1");
        }
    }
}
