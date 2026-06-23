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

        var expectedHost = new Uri(server.Url).Host;
        var expectedClientId = nats.ServerInfo!.ClientId.ToString();
        AssertActivityData("foo", tracker.Started, expectedHost, expectedClientId);
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

        // Intentionally not calling ConnectAsync first; the first publish triggers connect.
        // Counter must not record measurements with (Unset) server tags.
        for (var i = 0; i < 5; i++)
        {
            await nats.PublishAsync("foo", i, cancellationToken: cts.Token);
        }

        var published = meter.LongMeasurements
            .Where(m => m.Name == "messaging.client.published.messages")
            .ToList();

        published.Sum(m => m.Value).Should().Be(5);

        // Every measurement must carry the full server tag set, including the first publish
        // that triggered the connect.
        foreach (var m in published)
        {
            var tags = m.Tags.ToDictionary(t => t.Key, t => t.Value);
            tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
            tags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("publish");
            tags.Should().ContainKey("server.address");
            tags.Should().ContainKey("server.port");
            tags.Should().NotContainKey("messaging.destination.name");
            tags.Should().NotContainKey("messaging.nats.message.subject");
        }
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

        // SharedInbox so the connection doesn't open an inbox subscription at connect,
        // which would itself count as an active subscription and offset the sums below.
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestReplyMode = NatsRequestReplyMode.SharedInbox });

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
    public async Task Publish_operation_duration_histogram()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await nats.ConnectAsync();

        await nats.PublishAsync("foo.duration", 42, cancellationToken: cts.Token);

        var durations = meter.DoubleMeasurements
            .Where(m => m.Name == "messaging.client.operation.duration")
            .ToList();

        var publish = durations.Where(m => m.Tags.Any(t => t.Key == "messaging.operation" && (string?)t.Value == "publish")).ToList();
        publish.Should().NotBeEmpty();
        publish[0].Value.Should().BeGreaterThan(0);

        var tags = publish[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("server.address");
        tags.Should().ContainKey("server.port");
        tags.Should().NotContainKey("error.type");
    }

    [Fact]
    public async Task Request_operation_duration_histogram()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var sub = await nats.SubscribeCoreAsync<int>("foo.req", cancellationToken: cts.Token);
        var reg = sub.Register(async msg => await msg.ReplyAsync(msg.Data * 2, cancellationToken: cts.Token));

        var reply = await nats.RequestAsync<int, int>("foo.req", 21, cancellationToken: cts.Token);
        reply.Data.Should().Be(42);

        var request = meter.DoubleMeasurements
            .Where(m => m.Name == "messaging.client.operation.duration")
            .Where(m => m.Tags.Any(t => t.Key == "messaging.operation" && (string?)t.Value == "request"))
            .ToList();

        request.Should().HaveCount(1);
        request[0].Value.Should().BeGreaterThan(0);

        var tags = request[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().NotContainKey("error.type");

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Reconnect_counter()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        // Attach before ConnectAsync so we observe both opens and can wait for the second
        // (the reconnect) without racing the async event-channel delivery of the first.
        var opened = 0;
        var openedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        nats.ConnectionOpened += (_, _) =>
        {
            if (Interlocked.Increment(ref opened) == 2)
                openedTwice.TrySetResult();
            return default;
        };

        await nats.ConnectAsync();
        await nats.ReconnectAsync();
        await openedTwice.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The reconnects counter is incremented just after the ConnectionOpened event is
        // queued, so the measurement can land slightly after the event we awaited. Poll
        // for it rather than reading once and racing the increment.
        List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> reconnects = new();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            reconnects = meter.LongMeasurements
                .Where(m => m.Name == "nats.client.reconnects")
                .ToList();

            if (reconnects.Count > 0)
                break;

            await Task.Delay(25);
        }

        reconnects.Sum(m => m.Value).Should().Be(1);

        var tags = reconnects[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("reconnect");
        tags.Should().ContainKey("server.address");
        tags.Should().ContainKey("server.port");
    }

    [Fact]
    public async Task Sent_and_received_bytes_counters()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var sub = await nats.SubscribeCoreAsync<byte[]>("foo.bytes", cancellationToken: cts.Token);

        var payload = new byte[1024];
        await nats.PublishAsync("foo.bytes", payload, cancellationToken: cts.Token);

        var msg = await sub.Msgs.ReadAsync(cts.Token);
        msg.Data!.Length.Should().Be(1024);

        var sent = meter.LongMeasurements
            .Where(m => m.Name == "nats.client.sent.bytes")
            .Sum(m => m.Value);
        var received = meter.LongMeasurements
            .Where(m => m.Name == "nats.client.received.bytes")
            .Sum(m => m.Value);

        sent.Should().Be(1024);
        received.Should().Be(1024);

        var sentTags = meter.LongMeasurements.First(m => m.Name == "nats.client.sent.bytes").Tags.ToDictionary(t => t.Key, t => t.Value);
        sentTags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        sentTags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("publish");

        var recvTags = meter.LongMeasurements.First(m => m.Name == "nats.client.received.bytes").Tags.ToDictionary(t => t.Key, t => t.Value);
        recvTags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("receive");
    }

    [Fact]
    public async Task Subscribe_operation_duration_histogram()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var sub = await nats.SubscribeCoreAsync<int>("foo.sub.duration", cancellationToken: cts.Token);

        var subscribe = meter.DoubleMeasurements
            .Where(m => m.Name == "messaging.client.operation.duration")
            .Where(m => m.Tags.Any(t => t.Key == "messaging.operation" && (string?)t.Value == "subscribe"))
            .ToList();

        subscribe.Should().HaveCount(1);
        subscribe[0].Value.Should().BeGreaterThan(0);

        var tags = subscribe[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("server.address");
        tags.Should().ContainKey("server.port");
        tags.Should().NotContainKey("error.type");
    }

    [Fact]
    public async Task Request_operation_duration_records_error_type_on_failure()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var act = async () => await nats.RequestAsync<int, int>(
            "foo.no.responders",
            1,
            replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<NatsNoRespondersException>();

        var request = meter.DoubleMeasurements
            .Where(m => m.Name == "messaging.client.operation.duration")
            .Where(m => m.Tags.Any(t => t.Key == "messaging.operation" && (string?)t.Value == "request"))
            .ToList();

        request.Should().HaveCount(1);
        var tags = request[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("error.type");
        ((string?)tags["error.type"]).Should().Contain("NatsNoRespondersException");
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

    [Fact]
    public async Task Ack_operation_duration_histogram()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync(new StreamConfig { Name = "ack-stream", Subjects = ["ack.>"] }, cts.Token);
        await js.CreateOrUpdateConsumerAsync("ack-stream", new ConsumerConfig("ack-consumer"), cts.Token);

        await js.PublishAsync("ack.subject", "test-message", cancellationToken: cts.Token);

        var consumer = await js.GetConsumerAsync("ack-stream", "ack-consumer", cts.Token);

        await foreach (var msg in consumer.ConsumeAsync<string>(cancellationToken: cts.Token))
        {
            await msg.AckAsync(cancellationToken: cts.Token);
            break;
        }

        var ack = meter.DoubleMeasurements
            .Where(m => m.Name == "messaging.client.operation.duration")
            .Where(m => m.Tags.Any(t => t.Key == "messaging.operation" && (string?)t.Value == "ack"))
            .ToList();

        ack.Should().NotBeEmpty();
        ack[0].Value.Should().BeGreaterThan(0);

        var tags = ack[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().NotContainKey("error.type");
    }

    [Fact]
    public async Task Dropped_messages_counter()
    {
        using var meter = new MeterTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            SubPendingChannelCapacity = 3,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var dropped = 0;
        nats.MessageDropped += (_, _) =>
        {
            Interlocked.Increment(ref dropped);
            return default;
        };

        var sync = 0;
        var signal = new WaitSignal();

        // Block the consumer after the sync message so the bounded channel overflows.
        var subscription = Task.Run(
            async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<string>("drop.>", cancellationToken: cancellationToken))
                {
                    if (msg.Subject == "drop.sync")
                    {
                        Interlocked.Increment(ref sync);
                        await signal;
                        continue;
                    }

                    if (msg.Subject == "drop.end")
                    {
                        break;
                    }
                }
            },
            cancellationToken);

        await Retry.Until(
            "subscription is ready",
            () => Volatile.Read(ref sync) > 0,
            async () => await nats.PublishAsync("drop.sync", cancellationToken: cancellationToken));

        for (var i = 0; i < 20; i++)
        {
            await nats.PublishAsync("drop.data", $"msg{i}", cancellationToken: cancellationToken);
        }

        await Retry.Until("messages are dropped", () => Volatile.Read(ref dropped) > 0);

        signal.Pulse();
        await Retry.Until(
            "subscription ended",
            () => subscription.IsCompleted,
            async () => await nats.PublishAsync("drop.end", cancellationToken: cancellationToken));
        await subscription;

        var droppedMeasurements = meter.LongMeasurements
            .Where(m => m.Name == "nats.client.messages.dropped")
            .ToList();

        droppedMeasurements.Sum(m => m.Value).Should().BeGreaterThan(0);

        var tags = droppedMeasurements[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("messaging.system").WhoseValue.Should().Be("nats");
        tags.Should().ContainKey("messaging.operation").WhoseValue.Should().Be("receive");
    }

    [Fact]
    public async Task Inbox_subjects_collapsed_in_trace_tags()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var sub = await nats.SubscribeCoreAsync<int>("foo.inbox", cancellationToken: cts.Token);
        var reg = sub.Register(async msg => await msg.ReplyAsync(msg.Data * 2, cancellationToken: cts.Token));

        var reply = await nats.RequestAsync<int, int>("foo.inbox", 21, cancellationToken: cts.Token);
        reply.Data.Should().Be(42);

        // The request's reply-to is an inbox; it must be collapsed to "inbox" rather than
        // emitting the unique _INBOX.<nuid> value that would blow up backend tag cardinality.
        var replyToTags = tracker.Started
            .Select(a => a.GetTagItem("messaging.nats.message.reply_to") as string)
            .Where(v => v is not null)
            .ToList();

        replyToTags.Should().NotBeEmpty();
        replyToTags.Should().AllSatisfy(v => v.Should().Be("inbox"));

        // No high-cardinality tag should leak a raw inbox subject.
        string[] cardinalityTags =
        [
            "messaging.destination.name",
            "messaging.destination_publish.name",
            "messaging.nats.message.subject",
            "messaging.nats.message.reply_to",
        ];

        foreach (var activity in tracker.Started)
        {
            foreach (var tag in cardinalityTags)
            {
                if (activity.GetTagItem(tag) is string value)
                    value.Should().NotStartWith("_INBOX", $"{tag} should not carry a raw inbox subject");
            }
        }

        await sub.DisposeAsync();
        await reg;
    }

    private void AssertActivityData(string subject, IReadOnlyList<Activity> activityList, string expectedHost, string expectedClientId)
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

        // server.port and network.peer.port are integers per OTel semconv, not strings
        AssertIntTag(sendActivity, "server.port");
        AssertIntTag(sendActivity, "network.peer.port");
        AssertIntTag(receiveActivity, "server.port");
        AssertIntTag(receiveActivity, "network.peer.port");

        // server.address/network.peer.address come from the connect URI, not ServerInfo.Host
        // (the server bind address, often 0.0.0.0)
        Assert.Equal(expectedHost, sendActivity.GetTagItem("server.address"));
        Assert.Equal(expectedHost, sendActivity.GetTagItem("network.peer.address"));
        Assert.Equal(expectedHost, receiveActivity.GetTagItem("server.address"));
        Assert.Equal(expectedHost, receiveActivity.GetTagItem("network.peer.address"));

        // messaging.client_id is the server-assigned client id, cached per ServerInfo change
        Assert.Equal(expectedClientId, sendActivity.GetTagItem("messaging.client_id"));
        Assert.Equal(expectedClientId, receiveActivity.GetTagItem("messaging.client_id"));
    }

    private void AssertStringTagNotNullOrEmpty(Activity activity, string name)
    {
        var tag = activity.GetTagItem(name) as string;
        Assert.NotNull(tag);
        Assert.False(string.IsNullOrEmpty(tag));
    }

    private void AssertIntTag(Activity activity, string name)
    {
        var tag = activity.GetTagItem(name);
        Assert.NotNull(tag);
        Assert.IsType<int>(tag);
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
        private readonly object _sync = new();
        private readonly List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> _longMeasurements = new();
        private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _doubleMeasurements = new();

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

            // Measurement callbacks fire on background threads (e.g. the reconnect loop),
            // so guard the lists and hand out snapshots to readers.
            _listener.SetMeasurementEventCallback<long>((inst, val, tags, _) =>
            {
                lock (_sync)
                    _longMeasurements.Add((inst.Name, val, tags.ToArray()));
            });
            _listener.SetMeasurementEventCallback<double>((inst, val, tags, _) =>
            {
                lock (_sync)
                    _doubleMeasurements.Add((inst.Name, val, tags.ToArray()));
            });

            _listener.Start();
        }

        public IReadOnlyList<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> LongMeasurements
        {
            get
            {
                lock (_sync)
                    return _longMeasurements.ToArray();
            }
        }

        public IReadOnlyList<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> DoubleMeasurements
        {
            get
            {
                lock (_sync)
                    return _doubleMeasurements.ToArray();
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
