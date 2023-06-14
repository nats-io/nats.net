using System.Runtime.CompilerServices;
using NATS.Client.Core;

namespace NATS.Client.Core.Tests;

public static class WaitSignalExtensions
{
    public static Task ConnectionDisconnectedAsAwaitable(this NatsConnection connection)
    {
        var signal = new WaitSignal();
        connection.ConnectionDisconnected += (sender, e) =>
        {
            signal.Pulse();
        };
        return signal.Task.WaitAsync(signal.Timeout);
    }

    public static Task ConnectionOpenedAsAwaitable(this NatsConnection connection)
    {
        var signal = new WaitSignal();
        connection.ConnectionOpened += (sender, e) =>
        {
            signal.Pulse();
        };
        return signal.Task.WaitAsync(signal.Timeout);
    }

    public static Task ReconnectFailedAsAwaitable(this NatsConnection connection)
    {
        var signal = new WaitSignal();
        connection.ReconnectFailed += (sender, e) =>
        {
            signal.Pulse();
        };
        return signal.Task.WaitAsync(signal.Timeout);
    }
}

public class WaitSignal
{
    private TimeSpan _timeout;
    private int _count;
    private TaskCompletionSource _tcs;

    public WaitSignal()
        : this(TimeSpan.FromSeconds(10))
    {
    }

    public WaitSignal(int count)
        : this(TimeSpan.FromSeconds(10), count)
    {
    }

    public WaitSignal(TimeSpan timeout, int count = 1)
    {
        _timeout = timeout;
        _count = count;
        _tcs = new TaskCompletionSource();
    }

    public TimeSpan Timeout => _timeout;

    public Task Task => _tcs.Task;

    public void Pulse(Exception? exception = null)
    {
        if (exception == null)
        {
            if (Interlocked.Decrement(ref _count) > 0)
            {
                return;
            }

            _tcs.TrySetResult();
        }
        else
        {
            _tcs.TrySetException(exception);
        }
    }

    public TaskAwaiter GetAwaiter()
    {
        return _tcs.Task.WaitAsync(_timeout).GetAwaiter();
    }
}
