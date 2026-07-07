using System.Diagnostics;

namespace NATS.Client.Core.Tests;

/// <summary>
/// Listens to all "NATS.Net" activity sources and records started/stopped activities
/// so tests can inspect and assert on the telemetry the client emits.
/// </summary>
internal sealed class ActivityTracker : IDisposable
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
