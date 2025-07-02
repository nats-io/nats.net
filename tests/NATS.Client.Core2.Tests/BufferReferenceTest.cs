using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core2.Tests;

[Collection("nats-server")]
public class BufferReferenceTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public BufferReferenceTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Buffer_high_pressure_pub_test()
    {
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        await nats.ConnectRetryAsync();

        var prefix = _server.GetNextId();
        var data1 = new string('1', 65000);
        var data2 = new string('2', 65000);
        var data3 = new string('3', 65000);

        await using var sub = await nats.SubscribeCoreAsync<string>($"{prefix}.>");
        var subTask = Task.Run(async () =>
        {
            await foreach (var msg in sub.Msgs.ReadAllAsync())
            {
                if (msg.Subject == $"{prefix}.end")
                {
                    // This is the end signal, we can stop processing
                    break;
                }

                if (msg.Subject == $"{prefix}.1")
                {
                    Assert.Equal(data1, msg.Data);
                }

                if (msg.Subject == $"{prefix}.2")
                {
                    Assert.Equal(data2, msg.Data);
                }

                if (msg.Subject == $"{prefix}.3")
                {
                    Assert.Equal(data3, msg.Data);
                }
            }
        });

        // Publish a large number of messages in parallel
        var tasks = new List<Task>();
        for (var i = 0; i < 3; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (var j = 0; j < 1000; j++)
                {
                    await nats.PublishAsync($"{prefix}.1", data1);
                }

                await nats.PingAsync();
            }));
            tasks.Add(Task.Run(async () =>
            {
                for (var j = 0; j < 1000; j++)
                {
                    await nats.PublishAsync($"{prefix}.2", data2);
                }

                await nats.PingAsync();
            }));
            tasks.Add(Task.Run(async () =>
            {
                for (var j = 0; j < 1000; j++)
                {
                    await nats.PublishAsync($"{prefix}.3", data3);
                }

                await nats.PingAsync();
            }));
        }

        // Wait for all publish tasks to complete
        await Task.WhenAll(tasks);
        await nats.PingAsync();

        // Send an end signal to stop the subscriber
        await nats.PublishAsync($"{prefix}.end");

        // Wait for the subscriber to finish processing
        await subTask;
    }

    /// <summary>
    /// This test tries to force a race condition by creating a high-throughput environment
    /// with both large and small messages to increase buffer pool pressure.
    /// </summary>
    [Fact]
    public async Task Buffer_high_pressure_test()
    {
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        await nats.ConnectRetryAsync();

        var prefix = _server.GetNextId();

        // Create a set of subjects for different message sizes
        var largeSubject = $"{prefix}.buffer.large";
        var mediumSubject = $"{prefix}.buffer.medium";
        var smallSubject = $"{prefix}.buffer.small";

        // Create subscribers that echo back the message
        var largeSub = await nats.SubscribeCoreAsync<string>(largeSubject);
        var mediumSub = await nats.SubscribeCoreAsync<string>(mediumSubject);
        var smallSub = await nats.SubscribeCoreAsync<string>(smallSubject);

        var largeReg = largeSub.Register(msg => msg.ReplyAsync(msg.Data));
        var mediumReg = mediumSub.Register(msg => msg.ReplyAsync(msg.Data));
        var smallReg = smallSub.Register(msg => msg.ReplyAsync(msg.Data));

        // Create test data of various sizes to exercise different buffer allocations
        var largeData = new string('L', 65000);  // Just under the default max buffer size
        var mediumData = new string('M', 8192);  // Medium size
        var smallData = new string('S', 64);     // Small size

        // Run a large number of parallel requests of varying sizes
        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            // Add suffix to make each message unique
            var largeSuffix = $"-large-{i}";
            var mediumSuffix = $"-medium-{i}";
            var smallSuffix = $"-small-{i}";

            // Add large request tasks
            tasks.Add(Task.Run(async () =>
            {
                var request = largeData + largeSuffix;
                var reply = await nats.RequestAsync<string, string>(largeSubject, request);
                Assert.Equal(request, reply.Data);
            }));

            // Add medium request tasks
            tasks.Add(Task.Run(async () =>
            {
                var request = mediumData + mediumSuffix;
                var reply = await nats.RequestAsync<string, string>(mediumSubject, request);
                Assert.Equal(request, reply.Data);
            }));

            // Add small request tasks with higher frequency
            for (var j = 0; j < 5; j++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var request = smallData + smallSuffix + $"-{j}";
                    var reply = await nats.RequestAsync<string, string>(smallSubject, request);
                    Assert.Equal(request, reply.Data);
                }));
            }
        }

        // Wait for all tasks to complete - if there's a buffer corruption issue,
        // we'll likely see assertion failures
        await Task.WhenAll(tasks);

        await largeSub.DisposeAsync();
        await mediumSub.DisposeAsync();
        await smallSub.DisposeAsync();
        await largeReg;
        await mediumReg;
        await smallReg;
    }
}
