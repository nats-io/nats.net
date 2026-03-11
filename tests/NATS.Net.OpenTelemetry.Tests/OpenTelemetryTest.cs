using System.Diagnostics;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class OpenTelemetryTest
{
    private readonly ITestOutputHelper _output;

    public OpenTelemetryTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Publish_subscribe_activities()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var sub = await nats.SubscribeCoreAsync<string>("foo", cancellationToken: cts.Token);

        await nats.PublishAsync("foo", "bar", cancellationToken: cts.Token);

        await sub.Msgs.ReadAsync(cts.Token);

        AssertActivityData("foo", tracker.Started);
        tracker.AssertAllStopped();
    }

    [Fact]
    public async Task JetStream_consume_start_activity_with_interface()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync(new StreamConfig { Name = "test-stream", Subjects = ["test.>"] }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("test-stream", new ConsumerConfig("test-consumer"), cts.Token);

        await js.PublishAsync("test.subject", "test-message", cancellationToken: cts.Token);

        var consumer = await js.GetConsumerAsync("test-stream", "test-consumer", cts.Token);

        await foreach (var msg in consumer.ConsumeAsync<string>(cancellationToken: cts.Token))
        {
            // Test StartActivity on INatsJSMsg<T> interface (the fix for issue #1026)
            using var activity = msg.StartActivity("test.consume");
            Assert.NotNull(activity);
            Assert.Equal("test.consume", activity.OperationName);

            await msg.AckAsync(cancellationToken: cts.Token);
            break;
        }

        // Verify the publish activity was recorded
        var publishActivity = tracker.Started.FirstOrDefault(x => x.OperationName == "test.subject publish");
        Assert.NotNull(publishActivity);

        // Verify our custom activity was recorded
        var consumeActivity = tracker.Started.FirstOrDefault(x => x.OperationName == "test.consume");
        Assert.NotNull(consumeActivity);
        Assert.Equal(ActivityKind.Internal, consumeActivity.Kind);

        // Verify the parent relationship (consume activity should have publish as parent via trace context)
        Assert.Equal(publishActivity.TraceId, consumeActivity.TraceId);

        tracker.AssertAllStopped();
    }

    [Fact]
    public async Task Direct_request_reply_receive_activity_is_disposed()
    {
        using var tracker = new ActivityTracker();

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var sub = await nats.SubscribeCoreAsync<int>("foo.direct", cancellationToken: cts.Token);
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2, cancellationToken: cts.Token);
        });

        var reply = await nats.RequestAsync<int, int>("foo.direct", 21, cancellationToken: cts.Token);
        Assert.Equal(42, reply.Data);

        tracker.AssertAllStopped();

        await sub.DisposeAsync();
        await reg;
    }

    private void AssertActivityData(string subject, IReadOnlyList<Activity> activityList)
    {
        var activities = activityList.ToArray();
        Assert.NotEmpty(activities);

        foreach (var item in activities)
        {
            _output.WriteLine($"{item.Id}:");
            _output.WriteLine($"  OperationName: {item.OperationName}");
            _output.WriteLine($"  ParentId: {item.ParentId}");
            _output.WriteLine($"  Tags: {string.Join(", ", item.Tags.Select(x => $"{x.Key}: {x.Value}"))}");
            _output.WriteLine($"  Links: {string.Join(", ", item.Links.Select(x => $"{x.Context.TraceId}"))}");
        }

        var sendActivity = activities.First(x => x.OperationName == $"{subject} publish");
        var receiveActivity = activities.First(x => x.OperationName == $"{subject} receive");

        Assert.Equal(ActivityKind.Producer, sendActivity.Kind);
        Assert.Equal(ActivityKind.Consumer, receiveActivity.Kind);
        Assert.Equal(receiveActivity.ParentId, sendActivity.Id);

        AssertStringTagNotNullOrEmpty(sendActivity, "network.peer.address");
        AssertStringTagNotNullOrEmpty(sendActivity, "network.peer.address");
        AssertStringTagNotNullOrEmpty(sendActivity, "network.local.address");
        AssertStringTagNotNullOrEmpty(sendActivity, "server.address");

        // Verify network.transport is set on both activities
        Assert.Equal("tcp", sendActivity.GetTagItem("network.transport") as string);
        Assert.Equal("tcp", receiveActivity.GetTagItem("network.transport") as string);

        // Verify network.protocol.version is no longer present
        Assert.Null(sendActivity.GetTagItem("network.protocol.version"));
        Assert.Null(receiveActivity.GetTagItem("network.protocol.version"));
    }

    private void AssertStringTagNotNullOrEmpty(Activity activity, string name)
    {
        var tag = activity.GetTagItem(name) as string;
        Assert.NotNull(tag);
        Assert.False(string.IsNullOrEmpty(tag));
    }

    private sealed class ActivityTracker : IDisposable
    {
        private readonly List<Activity> _started = new();
        private readonly List<Activity> _stopped = new();
        private readonly ActivityListener _listener;

        public ActivityTracker()
        {
            _listener = new ActivityListener
            {
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ShouldListenTo = source => source.Name.StartsWith("NATS.Net"),
                ActivityStarted = _started.Add,
                ActivityStopped = _stopped.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> Started => _started;

        public IReadOnlyList<Activity> Stopped => _stopped;

        public void AssertAllStopped()
        {
            Assert.NotEmpty(_started);

            var leaked = _started
                .Where(started => !_stopped.Any(stopped => stopped.Id == started.Id))
                .ToList();

            if (leaked.Count > 0)
            {
                var details = string.Join("\n", leaked.Select(a => $"  [{a.Kind}] {a.OperationName} id={a.Id}"));
                Assert.Fail($"Activity leak detected. {leaked.Count} activity(s) started but never stopped:\n{details}");
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
