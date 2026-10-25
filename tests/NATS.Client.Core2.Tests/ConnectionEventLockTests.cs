using System.Reflection;
using System.Threading.Channels;
using NATS.Client.Core.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core2.Tests;

public class ConnectionEventLockTests
{
    [Fact]
    public async Task Connection_opened_event_is_queued_without_holding_the_connection_lock()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        // Observe the push from the thread that performs it. The event channel is swapped
        // for one that resumes its reader inline, so the ConnectionOpened handler runs on
        // the pushing thread and can inspect the connection lock at that moment.
        // No production code is changed to make this possible.
        using var observer = new PushObserver(nats);
        await observer.WaitUntilPublisherParkedAsync();

        // Positive control: an event queued while the lock is deliberately held must be
        // observed as held. This proves the observation reflects the pushing thread.
        await observer.VerifyObservationMechanismAsync();

        await nats.ConnectAsync();

        var observation = observer.SingleConnectionOpen();

        // The connection lock must not be held while the event is queued.
        observation.GateHeld.Should().BeFalse();
    }

    [Fact]
    public async Task Connection_opened_event_is_queued_without_holding_the_connection_lock_on_reconnect()
    {
        var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
            ReconnectJitter = TimeSpan.Zero,
        });

        using var observer = new PushObserver(nats);
        await observer.WaitUntilPublisherParkedAsync();
        await observer.VerifyObservationMechanismAsync();

        await nats.ConnectAsync();

        await server.StopAsync();
        await Retry.Until(
            "disconnected",
            () => nats.ConnectionState == NatsConnectionState.Reconnecting,
            timeout: TimeSpan.FromSeconds(30));

        server = await server.RestartAsync();

        await Retry.Until(
            "reconnected",
            () => observer.ConnectionOpens.Count >= 2,
            timeout: TimeSpan.FromSeconds(30));

        // The reconnect push must not happen under the lock either.
        observer.ConnectionOpens.Should().OnlyContain(o => !o.GateHeld);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Connection_events_are_reported_in_order_across_a_reconnect()
    {
        var server = await NatsServerProcess.StartAsync();
        var events = new EventLog();

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            ReconnectWaitMin = TimeSpan.FromMilliseconds(100),
            ReconnectWaitMax = TimeSpan.FromMilliseconds(100),
            ReconnectJitter = TimeSpan.Zero,
        });

        nats.ConnectionOpened += (_, _) =>
        {
            events.Add("opened");
            return default;
        };
        nats.ConnectionDisconnected += (_, _) =>
        {
            events.Add("disconnected");
            return default;
        };

        await nats.ConnectAsync();

        await server.StopAsync();
        await Retry.Until(
            "disconnect reported",
            () => events.Count("disconnected") >= 1,
            timeout: TimeSpan.FromSeconds(30));

        server = await server.RestartAsync();

        await Retry.Until(
            "reconnect reported",
            () => events.Count("opened") >= 2,
            timeout: TimeSpan.FromSeconds(30));

        // The open of a cycle is always reported before the disconnect that follows it.
        var order = events.Snapshot();
        order.IndexOf("opened").Should().BeLessThan(order.IndexOf("disconnected"));
        order.LastIndexOf("opened").Should().BeGreaterThan(order.IndexOf("disconnected"));

        // No event is duplicated and none is lost.
        events.Count("opened").Should().Be(2);
        events.Count("disconnected").Should().Be(1);
    }

    [Fact]
    public async Task Connection_opened_event_carries_the_connected_server_url()
    {
        await using var server = await NatsServerProcess.StartAsync();
        var opened = new EventLog();

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        nats.ConnectionOpened += (_, args) =>
        {
            opened.Add(args.Message);
            return default;
        };

        await nats.ConnectAsync();

        await Retry.Until(
            "open reported",
            () => opened.Snapshot().Count >= 1,
            timeout: TimeSpan.FromSeconds(30));

        // The payload is the connect URI, read while the connection state is consistent.
        var message = opened.Snapshot()[0];
        message.Should().NotBeNullOrEmpty();
        message.Should().StartWith("nats://");
        message.Should().Contain(server.Port.ToString());
    }

    [Fact]
    public async Task Connection_stays_usable_while_event_handlers_run()
    {
        await using var server = await NatsServerProcess.StartAsync();
        var handlerRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pingRtt = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        nats.ConnectionOpened += async (_, _) =>
        {
            handlerRunning.TrySetResult();

            // A handler that blocks must not wedge the connection.
            await handlerMayFinish.Task.ConfigureAwait(false);
            pingRtt.TrySetResult(await nats.PingAsync().ConfigureAwait(false));
        };

        await nats.ConnectAsync();

        await WithTimeout(handlerRunning.Task, TimeSpan.FromSeconds(30));

        // The connection answers while the event handler is still running.
        var rtt = await WithTimeout(nats.PingAsync().AsTask(), TimeSpan.FromSeconds(30));
        rtt.Should().BeGreaterThan(TimeSpan.Zero);

        handlerMayFinish.TrySetResult();
        (await WithTimeout(pingRtt.Task, TimeSpan.FromSeconds(30))).Should().BeGreaterThan(TimeSpan.Zero);
    }

    // Task.WaitAsync is not available on net481.
    private static async Task WithTimeout(Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task)
        {
            throw new TimeoutException("The operation has timed out.");
        }

        await task.ConfigureAwait(false);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        // Cast to Task so the non-generic overload is selected, otherwise this recurses.
        await WithTimeout((Task)task, timeout).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Observes the connection lock from the thread that pushes an event.
    /// </summary>
    /// <remarks>
    /// The connection queues events on a private unbounded channel. This helper swaps that
    /// channel for an equivalent one that resumes its reader inline, so a ConnectionOpened
    /// handler runs on the very thread that performed the push. The handler can then ask
    /// whether the connection lock is held by that thread.
    /// <para>
    /// This is a test-only observation mechanism: it changes no production code and no public
    /// behaviour. It reaches into private members, so it is coupled to the internals it observes
    /// and must be updated if they change.
    /// </para>
    /// </remarks>
    private readonly record struct Observation(string Message, int ThreadId, bool GateHeld);

    private sealed class PushObserver : IDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string CalibrationMessage = "__calibration__";
        private const string ControlMessage = "__control__";

        private readonly NatsConnection _connection;
        private readonly FieldInfo _eventChannelField;
        private readonly Channel<(NatsEvent, NatsEventArgs)> _originalChannel;
        private readonly Channel<(NatsEvent, NatsEventArgs)> _replacementChannel;
        private readonly object _gate;
        private readonly List<Observation> _observations = new();

        public PushObserver(NatsConnection connection)
        {
            _connection = connection;
            _eventChannelField = typeof(NatsConnection).GetField("_eventChannel", PrivateInstance)!;
            _originalChannel = (Channel<(NatsEvent, NatsEventArgs)>)_eventChannelField.GetValue(connection)!;
            _gate = typeof(NatsConnection).GetField("_gate", PrivateInstance)!.GetValue(connection)!;

            _replacementChannel = Channel.CreateUnbounded<(NatsEvent, NatsEventArgs)>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true,
                SingleWriter = false,
                SingleReader = true,
            });

            connection.ConnectionOpened += (_, args) =>
            {
                lock (_observations)
                    _observations.Add(new Observation(args.Message, Environment.CurrentManagedThreadId, IsGateHeldByCurrentThread()));
                return default;
            };

            _eventChannelField.SetValue(connection, _replacementChannel);

            // Wake the publisher so that it re-reads the field and moves to the replacement channel.
            _originalChannel.Writer.TryWrite((NatsEvent.ConnectionFailed, new NatsEventArgs("wake")));
        }

        /// <summary>
        /// The ConnectionOpened events observed, excluding the probe events.
        /// </summary>
        public IReadOnlyList<Observation> ConnectionOpens
        {
            get
            {
                lock (_observations)
                    return _observations.Where(o => o.Message != CalibrationMessage && o.Message != ControlMessage).ToArray();
            }
        }

        /// <summary>
        /// Waits until the connection's event publisher is parked on the replacement channel.
        /// </summary>
        /// <remarks>
        /// A publisher parked on the channel resumes inline on the thread that writes to it.
        /// That is what makes the observation meaningful, so this waits until a probe event is
        /// observed on the calling thread, and only then reports success.
        /// </remarks>
        public async Task WaitUntilPublisherParkedAsync()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                // The write happens on whatever thread the continuation is on, so the thread
                // has to be captured here, right before the write, not once up front.
                var writerThread = Environment.CurrentManagedThreadId;
                _replacementChannel.Writer.TryWrite((NatsEvent.ConnectionOpened, new NatsEventArgs(CalibrationMessage)));

                for (var wait = 0; wait < 10; wait++)
                {
                    await Task.Delay(20).ConfigureAwait(false);

                    lock (_observations)
                    {
                        var inline = _observations.Any(o => o.Message == CalibrationMessage && o.ThreadId == writerThread);

                        // Drop only the probe observations; real ones must survive.
                        _observations.RemoveAll(o => o.Message == CalibrationMessage);
                        if (inline)
                            return;
                    }
                }
            }

            throw new TimeoutException("The connection's event publisher did not park on the observed channel");
        }

        /// <summary>
        /// Proves that the observation reports the pushing thread's view of the lock.
        /// </summary>
        /// <remarks>
        /// An event is queued while the connection lock is deliberately held. The observation
        /// must report the lock as held, which can only happen if the handler ran on the thread
        /// that queued the event.
        /// </remarks>
        public async Task VerifyObservationMechanismAsync()
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var writerThread = Environment.CurrentManagedThreadId;

                EnterGate();
                try
                {
                    _replacementChannel.Writer.TryWrite((NatsEvent.ConnectionOpened, new NatsEventArgs(ControlMessage)));
                }
                finally
                {
                    ExitGate();
                }

                for (var wait = 0; wait < 10; wait++)
                {
                    await Task.Delay(20).ConfigureAwait(false);

                    Observation? control = null;
                    lock (_observations)
                    {
                        var index = _observations.FindIndex(o => o.Message == ControlMessage);
                        if (index >= 0)
                        {
                            control = _observations[index];
                            _observations.RemoveAt(index);
                        }
                    }

                    if (control is null)
                        continue;

                    if (control.Value.ThreadId != writerThread)
                    {
                        // The publisher was busy, so the handler did not run on the writing
                        // thread. That proves nothing; try again.
                        continue;
                    }

                    control.Value.GateHeld.Should().BeTrue(
                        "the observation must report the lock state of the thread that queued the event");
                    return;
                }
            }

            throw new TimeoutException("The control event was never observed on the thread that queued it");
        }

        public Observation SingleConnectionOpen()
        {
            var observations = ConnectionOpens;
            observations.Should().HaveCount(1);
            return observations[0];
        }

        /// <summary>
        /// Whether the connection lock is held by the calling thread.
        /// </summary>
        public bool IsGateHeldByCurrentThread()
        {
#if NET9_0_OR_GREATER
            return ((System.Threading.Lock)_gate).IsHeldByCurrentThread;
#else
            return Monitor.IsEntered(_gate);
#endif
        }

        public void Dispose()
        {
            // Restore the original channel so the connection keeps its normal behaviour.
            _eventChannelField.SetValue(_connection, _originalChannel);
            _replacementChannel.Writer.TryComplete();
        }

        private void EnterGate()
        {
#if NET9_0_OR_GREATER
            ((System.Threading.Lock)_gate).Enter();
#else
            Monitor.Enter(_gate);
#endif
        }

        private void ExitGate()
        {
#if NET9_0_OR_GREATER
            ((System.Threading.Lock)_gate).Exit();
#else
            Monitor.Exit(_gate);
#endif
        }
    }

    private sealed class EventLog
    {
        private readonly List<string> _entries = new();

        public void Add(string entry)
        {
            lock (_entries)
                _entries.Add(entry);
        }

        public int Count(string entry)
        {
            lock (_entries)
                return _entries.Count(e => e == entry);
        }

        public List<string> Snapshot()
        {
            lock (_entries)
                return _entries.ToList();
        }
    }
}
