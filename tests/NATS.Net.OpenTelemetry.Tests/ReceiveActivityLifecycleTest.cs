using System.Diagnostics;
using NATS.Client.JetStream;
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
}
