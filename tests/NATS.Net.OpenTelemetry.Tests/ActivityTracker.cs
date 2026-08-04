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
    private readonly object _sync = new();

    public ActivityTracker()
    {
        _listener = new ActivityListener
        {
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ShouldListenTo = source => source.Name.StartsWith("NATS.Net"),

            // Activities start and stop on the connection's read loop and on consumer
            // loops, so these callbacks run concurrently with the test thread. Plain
            // List.Add from several threads loses entries and makes the counts lie.
            ActivityStarted = a =>
            {
                lock (_sync)
                    _started.Add(a);
            },
            ActivityStopped = a =>
            {
                lock (_sync)
                    _stopped.Add(a);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Started
    {
        get
        {
            lock (_sync)
                return _started.ToArray();
        }
    }

    public IReadOnlyList<Activity> Stopped
    {
        get
        {
            lock (_sync)
                return _stopped.ToArray();
        }
    }

    public void AssertAllStopped()
    {
        Assert.NotEmpty(Started);

        var leaked = Leaked();

        if (leaked.Count > 0)
        {
            var details = string.Join("\n", leaked.Select(a => $"  [{a.Kind}] {a.OperationName} id={a.Id}"));
            Assert.Fail($"Activity leak detected. {leaked.Count} activity(s) started but never stopped:\n{details}");
        }
    }

    public void Dispose() => _listener.Dispose();

    private List<Activity> Leaked()
    {
        lock (_sync)
        {
            var stopped = new HashSet<string>(_stopped.Select(a => a.Id!));
            return _started.Where(a => !stopped.Contains(a.Id!)).ToList();
        }
    }
}
