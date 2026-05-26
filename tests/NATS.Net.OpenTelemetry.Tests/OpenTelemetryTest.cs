using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    public async Task Publish_counter()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await nats.ConnectAsync();

        for (var i = 0; i < 5; i++)
        {
            await nats.PublishAsync("foo", i, cancellationToken: cts.Token);
        }

        var published = meter.LongMeasurements
            .Where(m => m.Name == "messaging.client.published.messages")
            .ToList();

        published.Sum(m => m.Value).Should().Be(5);

        var tags = published[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("publish");
        tags.Should().ContainKey("server.address");
        tags.Should().ContainKey("server.port");
        tags.Should().NotContainKey("messaging.destination.name");
        tags.Should().NotContainKey("messaging.nats.message.subject");
    }

    [Fact]
    public async Task Consume_counter()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var sub = await nats.SubscribeCoreAsync<int>("foo.consume", cancellationToken: cts.Token);

        for (var i = 0; i < 5; i++)
        {
            await nats.PublishAsync("foo.consume", i, cancellationToken: cts.Token);
        }

        for (var i = 0; i < 5; i++)
        {
            await sub.Msgs.ReadAsync(cts.Token);
        }

        var consumed = meter.LongMeasurements
            .Where(m => m.Name == "messaging.client.consumed.messages")
            .ToList();

        consumed.Sum(m => m.Value).Should().Be(5);

        var tags = consumed[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("receive");
        tags.Should().ContainKey("server.address");
        tags.Should().ContainKey("server.port");
        tags.Should().NotContainKey("messaging.destination.name");
        tags.Should().NotContainKey("messaging.nats.message.subject");
    }

    [Fact]
    public async Task Consume_counter_direct_request_reply()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var sub = await nats.SubscribeCoreAsync<int>("foo.direct.consume", cancellationToken: cts.Token);
        var reg = sub.Register(async msg => await msg.ReplyAsync(msg.Data * 2, cancellationToken: cts.Token));

        var reply = await nats.RequestAsync<int, int>("foo.direct.consume", 21, cancellationToken: cts.Token);
        reply.Data.Should().Be(42);

        var consumed = meter.LongMeasurements
            .Where(m => m.Name == "messaging.client.consumed.messages")
            .Sum(m => m.Value);

        consumed.Should().Be(2);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Active_subscriptions_updown_counter()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var sub1 = await nats.SubscribeCoreAsync<int>("foo.active.1", cancellationToken: cts.Token);
        var sub2 = await nats.SubscribeCoreAsync<int>("foo.active.2", cancellationToken: cts.Token);

        var active = meter.LongMeasurements
            .Where(m => m.Name == "nats.client.active_subscriptions")
            .ToList();

        active.Sum(m => m.Value).Should().Be(2);

        var tags = active[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("subscribe");
        tags.Should().ContainKey("server.address");
        tags.Should().ContainKey("server.port");

        await sub1.DisposeAsync();

        meter.LongMeasurements
            .Where(m => m.Name == "nats.client.active_subscriptions")
            .Sum(m => m.Value).Should().Be(1);

        await sub2.DisposeAsync();

        meter.LongMeasurements
            .Where(m => m.Name == "nats.client.active_subscriptions")
            .Sum(m => m.Value).Should().Be(0);
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

    private sealed class MeterTracker : IDisposable
    {
        private readonly MeterListener _listener;

        public MeterTracker()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == "NATS.Net")
                        listener.EnableMeasurementEvents(instrument);
                },
            };

            _listener.SetMeasurementEventCallback<long>((inst, val, tags, _) =>
                LongMeasurements.Add((inst.Name, val, tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>((inst, val, tags, _) =>
                DoubleMeasurements.Add((inst.Name, val, tags.ToArray())));

            _listener.Start();
        }

        public List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> LongMeasurements { get; } = new();

        public List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> DoubleMeasurements { get; } = new();

        public void Dispose() => _listener.Dispose();
    }
}
