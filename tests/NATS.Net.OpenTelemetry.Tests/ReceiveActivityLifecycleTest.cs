using System.Diagnostics;
using System.Text;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NATS.Client.ObjectStore;
using NATS.Client.Services;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class ReceiveActivityLifecycleTest
{
    [Fact]
    public async Task Receive_activities_do_not_chain_in_direct_request_reply()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // JS API replies come from the server without trace context headers. In direct
        // mode the reply message is materialized synchronously on the connection's read
        // loop, so if starting its receive activity leaks into the read loop's
        // Activity.Current, each reply activity parents the previous one and the whole
        // chain stays reachable through the reader's execution context.
        var js = new NatsJSContext(nats);
        for (var i = 0; i < 5; i++)
        {
            await js.GetAccountInfoAsync(cts.Token);
        }

        var consumers = tracker.StartedFor(server.Port).Where(a => a.Kind == ActivityKind.Consumer).ToList();
        consumers.Should().HaveCount(5);
        consumers.Should().AllSatisfy(a => a.Parent.Should().BeNull(
            "receive activities must parent only by context, never by holding the ambient Activity"));

        // Each reply belongs to its own request, so they must land in five distinct
        // traces. Chaining would collapse them into one.
        consumers.Select(a => a.TraceId).Distinct().Should().HaveCount(5);

        tracker.AssertAllStopped(server.Port);
    }

    [Fact]
    public async Task Reply_receive_activity_is_traced_under_its_request()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Connect outside any activity so the read loop cannot capture one, leaving the
        // request context as the only thing that can parent the reply.
        await nats.ConnectAsync();

        var js = new NatsJSContext(nats);
        await js.GetAccountInfoAsync(cts.Token);

        var activities = tracker.StartedFor(server.Port);
        var request = activities.Single(a => a.OperationName.EndsWith(" request", StringComparison.Ordinal));
        var receive = activities.Single(a => a.Kind == ActivityKind.Consumer);

        // The reply carries no trace context of its own, so it takes the request as
        // parent: one trace covering the request, its publish and the reply.
        receive.TraceId.Should().Be(request.TraceId);
        receive.ParentSpanId.Should().Be(request.SpanId);

        // Parented by context, not by reference; a reference is what leaked.
        receive.Parent.Should().BeNull();

        tracker.AssertAllStopped(server.Port);
    }

    [Fact]
    public async Task Receive_activities_do_not_join_the_trace_ambient_at_connect_time()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var appListener = new ActivityListener();
        appListener.ShouldListenTo = source => source.Name == "test-app-connect";
        appListener.Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded;
        ActivitySource.AddActivityListener(appListener);
        using var appSource = new ActivitySource("test-app-connect");

        // The read loop is started by ConnectAsync and captures the caller's execution
        // context, Activity.Current included. Applications commonly connect lazily inside
        // a request span; every later receive activity must not be attributed to that
        // request's trace, which would also keep the request's activity alive for as long
        // as the connection lives.
        var ambient = appSource.StartActivity("user-request");
        ambient.Should().NotBeNull();

        await nats.ConnectAsync();

        ambient!.Stop();

        var js = new NatsJSContext(nats);
        for (var i = 0; i < 3; i++)
        {
            await js.GetAccountInfoAsync(cts.Token);
        }

        var receives = tracker.StartedFor(server.Port).Where(a => a.Kind == ActivityKind.Consumer).ToList();
        receives.Should().NotBeEmpty();
        receives.Should().AllSatisfy(a => a.TraceId.Should().NotBe(
            ambient.TraceId,
            "receive activities must not inherit the trace that happened to be current when the connection was established"));

        tracker.AssertAllStopped(server.Port);
    }

    [Fact]
    public async Task Service_endpoint_receive_span_is_exported_and_covers_the_handler()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var handlerSource = new ActivitySource("test-svc-handler");
        using var handlerListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "test-svc-handler",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(handlerListener);

        Activity? handlerSpan = null;

        var svc = new NatsSvcContext(nats);
        await using var service = await svc.AddServiceAsync("demo", "1.0.0", cancellationToken: cts.Token);
        await service.AddEndpointAsync<int>(
            name: "demo-echo",
            subject: "demo.echo",
            handler: async msg =>
            {
                handlerSpan = handlerSource.StartActivity("handler-work");
                handlerSpan?.Stop();
                await msg.ReplyAsync(msg.Data, cancellationToken: cts.Token);
            },
            cancellationToken: cts.Token);

        var reply = await nats.RequestAsync<int, int>("demo.echo", 7, cancellationToken: cts.Token);
        reply.Data.Should().Be(7);

        // The endpoint consumes the message itself, so nothing used to end its receive
        // activity and the span was never exported. A service request showed the caller's
        // request, publish and reply spans and nothing for the endpoint handling it.
        var endpointReceive = tracker.Stopped
            .Where(a => a.Kind == ActivityKind.Consumer)
            .Single(a => a.GetTagItem("messaging.nats.message.subject") as string == "demo.echo");

        endpointReceive.Duration.Should().BeGreaterThan(TimeSpan.Zero);

        // The endpoint span is the server side of the request, so the handler's own work
        // belongs under it.
        handlerSpan.Should().NotBeNull();
        handlerSpan!.ParentSpanId.Should().Be(endpointReceive.SpanId);
    }

    [Fact]
    public async Task Service_endpoint_receive_span_records_a_handler_failure()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var svc = new NatsSvcContext(nats);
        await using var service = await svc.AddServiceAsync("boom", "1.0.0", cancellationToken: cts.Token);
        await service.AddEndpointAsync<int>(
            name: "boom-endpoint",
            subject: "boom.endpoint",
            handler: _ => throw new InvalidOperationException("handler exploded"),
            cancellationToken: cts.Token);

        var reply = await nats.RequestAsync<int, int>("boom.endpoint", 1, cancellationToken: cts.Token);
        reply.Headers.Should().NotBeNull();
        reply.Headers!["Nats-Service-Error-Code"].ToString().Should().Be("999");

        var endpointReceive = tracker.Stopped
            .Where(a => a.Kind == ActivityKind.Consumer)
            .Single(a => a.GetTagItem("messaging.nats.message.subject") as string == "boom.endpoint");

        // No error status on the endpoint span was the third thing #1227 called out.
        endpointReceive.Status.Should().Be(ActivityStatusCode.Error);
        endpointReceive.Events.Should().Contain(e => e.Name == "exception");
    }

    [Fact]
    public async Task Key_value_watch_receive_spans_are_exported()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var kv = new NatsKVContext(new NatsJSContext(nats));
        var store = await kv.CreateStoreAsync("shape", cts.Token);

        var watched = new TaskCompletionSource();
        var watcher = Task.Run(
            async () =>
            {
                await foreach (var entry in store.WatchAsync<int>(cancellationToken: cts.Token))
                {
                    if (entry.Value == 42)
                    {
                        watched.TrySetResult();
                        return;
                    }
                }
            },
            cts.Token);

        await store.PutAsync("key", 42, cancellationToken: cts.Token);
        await watched.Task.WaitAsync(TimeSpan.FromSeconds(20), cts.Token);

        // The watcher consumes the message rather than handing it to an
        // ActivityEndingMsgReader, so its receive activity was created and never stopped.
        // Exporters are driven by ActivityStopped, so the span never existed at all.
        tracker.Stopped
            .Where(a => a.Kind == ActivityKind.Consumer)
            .Should().Contain(a => (a.GetTagItem("messaging.nats.message.subject") as string ?? string.Empty).StartsWith("$KV.shape", StringComparison.Ordinal));

        await watcher;
    }

    [Fact]
    public async Task Object_store_watch_receive_spans_are_exported()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var obj = new NatsObjContext(new NatsJSContext(nats));
        var store = await obj.CreateObjectStoreAsync("blobs", cts.Token);

        await store.PutAsync("thing", new MemoryStream(Encoding.UTF8.GetBytes("payload")), cancellationToken: cts.Token);

        // The get streams the object's chunks through the ordered push consumer, which is the
        // other internal consumer that never ended its receive activities.
        var target = new MemoryStream();
        await store.GetAsync("thing", target, cancellationToken: cts.Token);
        Encoding.UTF8.GetString(target.ToArray()).Should().Be("payload");

        tracker.Stopped
            .Where(a => a.Kind == ActivityKind.Consumer)
            .Should().Contain(a => (a.GetTagItem("messaging.nats.message.subject") as string ?? string.Empty).StartsWith("$O.blobs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Direct_mode_ends_the_reply_activity_when_no_responders_throws()
    {
        using var tracker = new ActivityTracker();
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await nats.ConnectAsync();

        // Direct mode builds the 503 sentinel reply on the read loop, which starts a receive
        // activity. RequestAsync ended it only on the path that returns a message, and with
        // ThrowIfNoResponders set this path throws instead, so the span was never exported.
        var act = async () => await nats.RequestAsync<int, int>(
            "nobody.listening",
            1,
            replyOpts: new NatsSubOpts { ThrowIfNoResponders = true },
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<NatsNoRespondersException>();

        tracker.AssertAllStopped(server.Port);
    }
}
