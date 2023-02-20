using System.Runtime.CompilerServices;
using NATS.Client.Core;

namespace NATS.Client.Core.Tests;

public class WaitSignal
{
    TimeSpan timeout;
    TaskCompletionSource tcs;

    public TimeSpan Timeout => timeout;
    public Task Task => tcs.Task;

    public WaitSignal()
        : this(TimeSpan.FromSeconds(10))
    {
    }

    public WaitSignal(TimeSpan timeout)
    {
        this.timeout = timeout;
        this.tcs = new TaskCompletionSource();
    }

    public void Pulse(Exception? exception = null)
    {
        if (exception == null)
        {
            tcs.TrySetResult();
        }
        else
        {
            tcs.TrySetException(exception);
        }
    }

    public TaskAwaiter GetAwaiter()
    {
        return tcs.Task.WaitAsync(timeout).GetAwaiter();
    }
}

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
