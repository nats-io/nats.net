using System.Diagnostics;

namespace NATS.Client.Core.Tests;

/// <summary>
/// Listens to all "NATS.Net" activity sources and records started/stopped activities
/// so tests can inspect and assert on the telemetry the client emits.
/// </summary>
/// <remarks>
/// ActivityListener is process-global and xUnit runs test classes concurrently, so a tracker
/// also sees activities belonging to other test classes: receive activities in particular are
/// started on connection read loops, which are ordinary background threads outside xUnit's
/// concurrency control. Anything asserting on counts or on the absence of activities must
/// therefore scope itself to its own connection, which the ServerPort overloads do since each
/// test starts its own nats-server on its own port.
/// </remarks>
internal sealed class ActivityTracker : IDisposable
{
    private const string ServerPortTag = "server.port";

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

    /// <summary>
    /// Activities started by a connection to the given server port.
    /// </summary>
    public IReadOnlyList<Activity> StartedFor(int serverPort) => Started.Where(a => IsForServer(a, serverPort)).ToList();

    public void AssertAllStopped(int serverPort)
    {
        var started = StartedFor(serverPort);
        Assert.NotEmpty(started);

        List<Activity> leaked;
        lock (_sync)
        {
            var stopped = new HashSet<string>(_stopped.Select(a => a.Id!));
            leaked = started.Where(a => !stopped.Contains(a.Id!)).ToList();
        }

        if (leaked.Count > 0)
        {
            var details = string.Join("\n", leaked.Select(a => $"  [{a.Kind}] {a.OperationName} id={a.Id}"));
            Assert.Fail($"Activity leak detected. {leaked.Count} activity(s) started but never stopped:\n{details}");
        }
    }

    public void Dispose() => _listener.Dispose();

    private static bool IsForServer(Activity activity, int serverPort) => activity.GetTagItem(ServerPortTag) is int port && port == serverPort;
}
