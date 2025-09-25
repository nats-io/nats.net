using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ManageStreamTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ManageStreamTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Account_info_create_get_update_stream(NatsRequestReplyMode mode)
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });
        await nats.ConnectRetryAsync();

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

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task List_delete_stream(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId() + "-";
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        await js.CreateStreamAsync($"{prefix}s2", [$"{prefix}s2.*"], cts.Token);
        await js.CreateStreamAsync($"{prefix}s3", [$"{prefix}s3.*"], cts.Token);

        // List
        {
            var list = new List<StreamInfo>();
            await foreach (var stream in js.ListStreamsAsync(cancellationToken: cts.Token))
            {
                if (stream.Info.Config.Name!.StartsWith(prefix))
                    list.Add(stream.Info);
            }

            Assert.Equal(3, list.Count);
            Assert.Contains(list, s => s.Config.Name == $"{prefix}s1");
            Assert.Contains(list, s => s.Config.Name == $"{prefix}s2");
            Assert.Contains(list, s => s.Config.Name == $"{prefix}s3");
        }

        // Delete
        {
            var deleteResponse = await js.DeleteStreamAsync($"{prefix}s1", cts.Token);
            Assert.True(deleteResponse);

            var list = new List<StreamInfo>();
            await foreach (var stream in js.ListStreamsAsync(cancellationToken: cts.Token))
            {
                if (stream.Info.Config.Name!.StartsWith(prefix))
                    list.Add(stream.Info);
            }

            Assert.DoesNotContain(list, s => s.Config.Name == $"{prefix}s1");
        }
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Delete_one_msg(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        var stream = await js.GetStreamAsync($"{prefix}s1", new StreamInfoRequest { SubjectsFilter = $"{prefix}s1.Ü" }, cts.Token);
        Assert.Null(stream.Info.State.Subjects);

        await js.PublishAsync($"{prefix}s1.1", new byte[] { 1 }, cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1.2", new byte[] { 2 }, cancellationToken: cts.Token);
        await js.PublishAsync($"{prefix}s1.3", new byte[] { 3 }, cancellationToken: cts.Token);

        stream = await js.GetStreamAsync($"{prefix}s1", new StreamInfoRequest { SubjectsFilter = $"{prefix}s1.*" }, cts.Token);

        Assert.Equal(3, stream.Info.State.Subjects?.Count);

        var deleteResponse = await js.DeleteMessageAsync($"{prefix}s1", new StreamMsgDeleteRequest { Seq = 1 }, cts.Token);
        Assert.True(deleteResponse.Success);

        stream = await js.GetStreamAsync($"{prefix}s1", new StreamInfoRequest { SubjectsFilter = $"{prefix}s1.*" }, cts.Token);

        Assert.Equal(2, stream.Info.State.Subjects?.Count);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create_or_update_stream_should_be_create_stream_if_stream_doesnt_exist(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = mode });
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var streamConfig = new StreamConfig("s1", ["s1.*"])
        { Storage = StreamConfigStorage.File };

        var accountInfoBefore = await js.GetAccountInfoAsync(cts.Token);
        await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);
        var accountInfoAfter = await js.GetAccountInfoAsync(cts.Token);

        Assert.Equal(0, accountInfoBefore.Streams);
        Assert.Equal(1, accountInfoAfter.Streams);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create_or_update_stream_should_be_update_stream_if_stream_exist(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var streamConfig = new StreamConfig($"{prefix}s1", [$"{prefix}s1.*"])
        { Storage = StreamConfigStorage.File, NoAck = false };
        var streamConfigForUpdated = streamConfig with { NoAck = true };

        var stream = await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);
        var updatedStream = await js.CreateOrUpdateStreamAsync(streamConfigForUpdated, cts.Token);

        Assert.False(stream.Info.Config.NoAck);
        Assert.True(updatedStream.Info.Config.NoAck);
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create_or_update_stream_should_be_throwing_update_operation_errors(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var streamConfig = new StreamConfig($"{prefix}s1", [$"{prefix}s1.*"])
        { Storage = StreamConfigStorage.File };
        var streamConfigForUpdated = streamConfig with { Storage = StreamConfigStorage.Memory };

        await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);
        await Assert.ThrowsAsync<NatsJSApiException>(async () => await js.CreateOrUpdateStreamAsync(streamConfigForUpdated, cts.Token));
    }

    [SkipIfNatsServer(versionEarlierThan: "2.12")]
    public async Task PersistMode_property_should_be_set_on_stream()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Test 1: Create a stream with PersistMode set to Async
        var streamConfigAsync = new StreamConfig($"{prefix}persist-async", [$"{prefix}persist-async.*"])
        {
            PersistMode = StreamConfigPersistMode.Async,
        };

        var streamAsync = await js.CreateStreamAsync(streamConfigAsync, cts.Token);

        // Verify the property is set on the created stream
        Assert.Equal(StreamConfigPersistMode.Async, streamAsync.Info.Config.PersistMode);

        // Get the stream and verify the property is persisted
        var retrievedStreamAsync = await js.GetStreamAsync($"{prefix}persist-async", cancellationToken: cts.Token);
        Assert.Equal(StreamConfigPersistMode.Async, retrievedStreamAsync.Info.Config.PersistMode);

        // Test 2: Create a stream with PersistMode set to Default
        var streamConfigDefault = new StreamConfig($"{prefix}persist-default", [$"{prefix}persist-default.*"])
        {
            PersistMode = StreamConfigPersistMode.Default,
        };

        var streamDefault = await js.CreateStreamAsync(streamConfigDefault, cts.Token);

        // Verify the property is set on the created stream
        Assert.Equal(StreamConfigPersistMode.Default, streamDefault.Info.Config.PersistMode);

        // Get the stream and verify the property is persisted
        var retrievedStreamDefault = await js.GetStreamAsync($"{prefix}persist-default", cancellationToken: cts.Token);
        Assert.Equal(StreamConfigPersistMode.Default, retrievedStreamDefault.Info.Config.PersistMode);

        // Test 3: Verify that updating PersistMode throws an exception
        var updatedConfig = streamConfigAsync with { PersistMode = StreamConfigPersistMode.Default };
        var exception = await Assert.ThrowsAsync<NatsJSApiException>(
            async () => await js.UpdateStreamAsync(updatedConfig, cts.Token));

        // Verify the error message indicates persist mode cannot be changed
        Assert.Contains("persist mode", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(500, exception.Error.Code);
        Assert.Equal(10052, exception.Error.ErrCode);
        Assert.Equal("stream configuration update can not change persist mode", exception.Error.Description);

        var updatedAsync = await js.GetStreamAsync($"{prefix}persist-async", cancellationToken: cts.Token);
        Assert.Equal(StreamConfigPersistMode.Async, updatedAsync.Info.Config.PersistMode);
    }
}
