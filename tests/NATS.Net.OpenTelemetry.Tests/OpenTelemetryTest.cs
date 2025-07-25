using System.Diagnostics;
using NATS.Client.Platform.Windows.Tests;

namespace NATS.Client.Core.Tests;

public class OpenTelemetryTest
{
    private readonly ITestOutputHelper _output;

    public OpenTelemetryTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Publish_subscribe_activities()
    {
        var activities = new List<Activity>();
        using var activityListener = StartActivityListener(activities);
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var sub = await nats.SubscribeCoreAsync<string>("foo", cancellationToken: cts.Token);

        await nats.PublishAsync("foo", "bar", cancellationToken: cts.Token);

        await sub.Msgs.ReadAsync(cts.Token);

        AssertActivityData("foo", activities);
    }

    private static ActivityListener StartActivityListener(List<Activity> activities)
    {
        var activityListener = new ActivityListener();
        activityListener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded;
        activityListener.SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded;
        activityListener.ShouldListenTo = activitySource => activitySource.Name.StartsWith("NATS.Net");
        activityListener.ActivityStarted = activities.Add;
        ActivitySource.AddActivityListener(activityListener);
        return activityListener;
    }

    private void AssertActivityData(string subject, List<Activity> activityList)
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
    }

    private void AssertStringTagNotNullOrEmpty(Activity activity, string name)
    {
        var tag = activity.GetTagItem(name) as string;
        Assert.NotNull(tag);
        Assert.False(string.IsNullOrEmpty(tag));
    }
}
