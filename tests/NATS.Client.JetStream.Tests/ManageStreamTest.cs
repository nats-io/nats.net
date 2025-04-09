using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.Platform.Windows.Tests;

namespace NATS.Client.JetStream.Tests;

public class ManageStreamTest
{
    private readonly ITestOutputHelper _output;

    public ManageStreamTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Account_info_create_get_update_stream()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        // Account Info
        {
            var accountInfo = await js.GetAccountInfoAsync(cancellationToken);
            Assert.Equal(0, accountInfo.Streams);
        }

        // Create
        {
            var stream = await js.CreateStreamAsync(
                config: new StreamConfig { Name = "events", Subjects = new[] { "events.*" } },
                cancellationToken: cancellationToken);
            Assert.Equal("events", stream.Info.Config.Name);

            var accountInfo = await js.GetAccountInfoAsync(cancellationToken);
            Assert.Equal(1, accountInfo.Streams);
        }

        // Get
        {
            var stream = await js.GetStreamAsync("events", cancellationToken: cancellationToken);
            Assert.Equal("events", stream.Info.Config.Name);
            Assert.Equal(new[] { "events.*" }, stream.Info.Config.Subjects);
        }

        // Update
        {
            var stream1 = await js.GetStreamAsync("events", cancellationToken: cancellationToken);
            Assert.Equal(-1, stream1.Info.Config.MaxMsgs);

            var stream2 = await js.UpdateStreamAsync(new StreamConfig { Name = "events", MaxMsgs = 10 }, cancellationToken);
            Assert.Equal(10, stream2.Info.Config.MaxMsgs);

            var stream3 = await js.GetStreamAsync("events", cancellationToken: cancellationToken);
            Assert.Equal(10, stream3.Info.Config.MaxMsgs);
        }
    }

    [Fact]
    public async Task List_delete_stream()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateStreamAsync("s2", new[] { "s2.*" }, cts.Token);
        await js.CreateStreamAsync("s3", new[] { "s3.*" }, cts.Token);

        // List
        {
            var list = new List<StreamInfo>();
            await foreach (var stream in js.ListStreamsAsync(cancellationToken: cts.Token))
            {
                list.Add(stream.Info);
            }

            Assert.Equal(3, list.Count);
            Assert.Contains(list, s => s.Config.Name == "s1");
            Assert.Contains(list, s => s.Config.Name == "s2");
            Assert.Contains(list, s => s.Config.Name == "s3");
        }

        // Delete
        {
            var deleteResponse = await js.DeleteStreamAsync("s1", cts.Token);
            Assert.True(deleteResponse);

            var list = new List<StreamInfo>();
            await foreach (var stream in js.ListStreamsAsync(cancellationToken: cts.Token))
            {
                list.Add(stream.Info);
            }

            Assert.DoesNotContain(list, s => s.Config.Name == "s1");
        }
    }

    [Fact]
    public async Task Delete_one_msg()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        var stream = await js.GetStreamAsync("s1", new StreamInfoRequest() { SubjectsFilter = "s1.Ü" }, cts.Token);
        Assert.Null(stream.Info.State.Subjects);

        await js.PublishAsync("s1.1", new byte[] { 1 }, cancellationToken: cts.Token);
        await js.PublishAsync("s1.2", new byte[] { 2 }, cancellationToken: cts.Token);
        await js.PublishAsync("s1.3", new byte[] { 3 }, cancellationToken: cts.Token);

        stream = await js.GetStreamAsync("s1", new StreamInfoRequest() { SubjectsFilter = "s1.*" }, cts.Token);

        Assert.Equal(3, stream.Info.State.Subjects?.Count);

        var deleteResponse = await js.DeleteMessageAsync("s1", new StreamMsgDeleteRequest { Seq = 1 }, cts.Token);
        Assert.True(deleteResponse.Success);

        stream = await js.GetStreamAsync("s1", new StreamInfoRequest() { SubjectsFilter = "s1.*" }, cts.Token);

        Assert.Equal(2, stream.Info.State.Subjects?.Count);
    }

    [Fact]
    public async Task Create_or_update_stream_should_be_create_stream_if_stream_doesnt_exist()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        var streamConfig = new StreamConfig("s1", ["s1.*"])
        { Storage = StreamConfigStorage.File };

        var accountInfoBefore = await js.GetAccountInfoAsync(cts.Token);
        await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);
        var accountInfoAfter = await js.GetAccountInfoAsync(cts.Token);

        Assert.Equal(0, accountInfoBefore.Streams);
        Assert.Equal(1, accountInfoAfter.Streams);
    }

    [Fact]
    public async Task Create_or_update_stream_should_be_update_stream_if_stream_exist()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        var streamConfig = new StreamConfig("s1", ["s1.*"])
        { Storage = StreamConfigStorage.File, NoAck = false };
        var streamConfigForUpdated = streamConfig with { NoAck = true };

        var stream = await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);
        var updatedStream = await js.CreateOrUpdateStreamAsync(streamConfigForUpdated, cts.Token);

        Assert.False(stream.Info.Config.NoAck);
        Assert.True(updatedStream.Info.Config.NoAck);
    }

    [Fact]
    public async Task Create_or_update_stream_should_be_throwing_update_operation_errors()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        var streamConfig = new StreamConfig("s1", ["s1.*"])
        { Storage = StreamConfigStorage.File };
        var streamConfigForUpdated = streamConfig with { Storage = StreamConfigStorage.Memory };

        await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);
        await Assert.ThrowsAsync<NatsJSApiException>(async () => await js.CreateOrUpdateStreamAsync(streamConfigForUpdated, cts.Token));
    }
}
