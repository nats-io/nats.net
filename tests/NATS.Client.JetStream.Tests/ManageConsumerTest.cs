using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.Platform.Windows.Tests;
using NATS.Client.TestUtilities;

namespace NATS.Client.JetStream.Tests;

public class ManageConsumerTest
{
    private readonly ITestOutputHelper _output;

    public ManageConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_get_consumer()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        // Create
        {
            var consumer = await js.CreateOrUpdateConsumerAsync(
                "s1",
                new ConsumerConfig("c1"),
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
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);
        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c2", cancellationToken: cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", "c3", cancellationToken: cts.Token);

        // List
        {
            var list = new List<INatsJSConsumer>();
            await foreach (var consumer in js.ListConsumersAsync("s1", cts.Token))
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
            var response = await js.DeleteConsumerAsync("s1", "c1", cts.Token);
            Assert.True(response);

            var list = new List<INatsJSConsumer>();
            await foreach (var consumer in js.ListConsumersAsync("s1", cts.Token))
            {
                list.Add(consumer);
            }

            Assert.Equal(2, list.Count);
            Assert.DoesNotContain(list, c => c.Info.Config.Name == "c1");
        }
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Pause_resume_consumer()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContextFactory().CreateContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);
        await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig("c1"), cts.Token);

        var pauseUntil = DateTimeOffset.Now.AddHours(1);

        // Pause
        {
            var consumerPauseResponse = await js.PauseConsumerAsync("s1", "c1", pauseUntil, cts.Token);

            Assert.True(consumerPauseResponse.IsPaused);
            Assert.Equal(pauseUntil, consumerPauseResponse.PauseUntil);

            var consumerInfo = await js.GetConsumerAsync("s1", "c1", cts.Token);
            Assert.True(consumerInfo.Info.IsPaused);
            Assert.Equal(pauseUntil, consumerInfo.Info.Config.PauseUntil);
        }

        // Resume
        {
            var isResumed = await js.ResumeConsumerAsync("s1", "c1", cts.Token);
            Assert.True(isResumed);

            var consumerInfo = await js.GetConsumerAsync("s1", "c1", cts.Token);
            Assert.False(consumerInfo.Info.IsPaused);
            Assert.Null(consumerInfo.Info.Config.PauseUntil);
        }
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task Consumer_create_update_action()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        var streamConfig = new StreamConfig { Name = "s1" };
        await js.CreateStreamAsync(streamConfig);

        // Create consumer
        {
            var consumerConfig = new ConsumerConfig { Name = "c1" };

            await js.CreateConsumerAsync("s1", consumerConfig);
        }

        // Try to create when consumer exactly
        {
            var changedConsumerConfig = new ConsumerConfig { Name = "c1", AckWait = TimeSpan.FromSeconds(10) };
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () => await js.CreateConsumerAsync("s1", changedConsumerConfig));

            Assert.Equal("consumer already exists", exception.Message);
            Assert.Equal(10148, exception.Error.ErrCode);
        }

        // Update consumer
        {
            var c1 = await js.GetConsumerAsync("s1", "c1");
            Assert.Equal(TimeSpan.FromSeconds(30), c1.Info.Config.AckWait);

            var changedConsumerConfig = new ConsumerConfig { Name = "c1", AckWait = TimeSpan.FromSeconds(10) };
            await js.UpdateConsumerAsync("s1", changedConsumerConfig);

            var c2 = await js.GetConsumerAsync("s1", "c1");
            Assert.Equal(TimeSpan.FromSeconds(10), c2.Info.Config.AckWait);
        }

        // Try to update when consumer does not exist
        {
            var notExistConsumerConfig = new ConsumerConfig { Name = "c2", AckWait = TimeSpan.FromSeconds(10) };
            var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () => await js.UpdateConsumerAsync("s1", notExistConsumerConfig));

            Assert.Equal("consumer does not exist", exception.Message);
            Assert.Equal(10149, exception.Error.ErrCode);
        }
    }
}
