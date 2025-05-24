using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ManageConsumerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ManageConsumerTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Create_get_consumer(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestTimeout = TimeSpan.FromSeconds(10), RequestReplyMode = mode });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);

        // Create
        {
            var consumer = await js.CreateOrUpdateConsumerAsync(
                $"{prefix}s1",
                new ConsumerConfig($"{prefix}c1"),
                cts.Token);
            Assert.Equal($"{prefix}s1", consumer.Info.StreamName);
            Assert.Equal($"{prefix}c1", consumer.Info.Config.Name);
        }

        // Get
        {
            var consumer = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
            Assert.Equal($"{prefix}s1", consumer.Info.StreamName);
            Assert.Equal($"{prefix}c1", consumer.Info.Config.Name);
        }
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task List_delete_consumer(NatsRequestReplyMode mode)
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync($"{prefix}s1", [$"{prefix}s1.*"], cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c1", cancellationToken: cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c2", cancellationToken: cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", $"{prefix}c3", cancellationToken: cts.Token);

        // List
        {
            var list = new List<INatsJSConsumer>();
            await foreach (var consumer in js.ListConsumersAsync($"{prefix}s1", cts.Token))
            {
                list.Add(consumer);
            }

            Assert.Equal(3, list.Count);
            Assert.True(list.All(c => c.Info.StreamName == $"{prefix}s1"));
            Assert.Contains(list, c => c.Info.Config.Name == $"{prefix}c1");
            Assert.Contains(list, c => c.Info.Config.Name == $"{prefix}c2");
            Assert.Contains(list, c => c.Info.Config.Name == $"{prefix}c3");
        }

        // Delete
        {
            var response = await js.DeleteConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
            Assert.True(response);

            var list = new List<INatsJSConsumer>();
            await foreach (var consumer in js.ListConsumersAsync($"{prefix}s1", cts.Token))
            {
                list.Add(consumer);
            }

            Assert.Equal(2, list.Count);
            Assert.DoesNotContain(list, c => c.Info.Config.Name == $"{prefix}c1");
        }
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Pause_resume_consumer()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContextFactory().CreateContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", [$"{prefix}s1.*"]), cts.Token);
        await js.CreateOrUpdateConsumerAsync($"{prefix}s1", new ConsumerConfig($"{prefix}c1"), cts.Token);

        var pauseUntil = DateTimeOffset.Now.AddHours(1);

        // Pause
        {
            var consumerPauseResponse = await js.PauseConsumerAsync($"{prefix}s1", $"{prefix}c1", pauseUntil, cts.Token);

            Assert.True(consumerPauseResponse.IsPaused);
            Assert.Equal(pauseUntil, consumerPauseResponse.PauseUntil);

            var consumerInfo = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
            Assert.True(consumerInfo.Info.IsPaused);
            Assert.Equal(pauseUntil, consumerInfo.Info.Config.PauseUntil);
        }

        // Resume
        {
            var isResumed = await js.ResumeConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
            Assert.True(isResumed);

            var consumerInfo = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1", cts.Token);
            Assert.False(consumerInfo.Info.IsPaused);
            Assert.Null(consumerInfo.Info.Config.PauseUntil);
        }
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task Consumer_create_update_action()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        await nats.ConnectRetryAsync();
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        var streamConfig = new StreamConfig { Name = $"{prefix}s1" };
        await js.CreateStreamAsync(streamConfig);

        // Create consumer
        {
            var consumerConfig = new ConsumerConfig { Name = $"{prefix}c1" };

            await js.CreateConsumerAsync($"{prefix}s1", consumerConfig);
        }

        // Try to create when consumer exactly
        {
            var changedConsumerConfig = new ConsumerConfig { Name = $"{prefix}c1", AckWait = TimeSpan.FromSeconds(10) };
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () => await js.CreateConsumerAsync($"{prefix}s1", changedConsumerConfig));

            Assert.Equal("consumer already exists", exception.Message);
            Assert.Equal(10148, exception.Error.ErrCode);
        }

        // Update consumer
        {
            var c1 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1");
            Assert.Equal(TimeSpan.FromSeconds(30), c1.Info.Config.AckWait);

            var changedConsumerConfig = new ConsumerConfig { Name = $"{prefix}c1", AckWait = TimeSpan.FromSeconds(10) };
            await js.UpdateConsumerAsync($"{prefix}s1", changedConsumerConfig);

            var c2 = await js.GetConsumerAsync($"{prefix}s1", $"{prefix}c1");
            Assert.Equal(TimeSpan.FromSeconds(10), c2.Info.Config.AckWait);
        }

        // Try to update when consumer does not exist
        {
            var notExistConsumerConfig = new ConsumerConfig { Name = $"{prefix}c2", AckWait = TimeSpan.FromSeconds(10) };
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () => await js.UpdateConsumerAsync($"{prefix}s1", notExistConsumerConfig));

            Assert.Equal("consumer does not exist", exception.Message);
            Assert.Equal(10149, exception.Error.ErrCode);
        }
    }
}
