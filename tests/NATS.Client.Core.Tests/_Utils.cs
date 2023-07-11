using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NATS.Client.Core.Tests;

public static class Retry
{
    public static async Task Until(string reason, Func<bool> condition, Func<Task>? action = null, TimeSpan? timeout = null, TimeSpan? retryDelay = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var delay1 = retryDelay ?? TimeSpan.FromSeconds(.1);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (action != null)
                await action();
            if (condition())
                return;
            await Task.Delay(delay1);
        }

        throw new TimeoutException($"Took too long ({timeout}) waiting until {reason}");
    }
}

public static class Net
{
    public static void WaitForTcpPortToClose(int port)
    {
        while (true)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(IPAddress.Loopback, port);
            }
            catch (SocketException)
            {
                return;
            }
        }
    }
}

internal static class NatsMsgTestUtils
{
    internal static Task Register<T>(this NatsSub<T>? sub, Action<NatsMsg<T>> action)
    {
        if (sub == null)
            return Task.CompletedTask;
        return Task.Run(async () =>
        {
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync())
            {
                action(natsMsg);
            }
        });
    }

    internal static Task Register<T>(this NatsSub<T>? sub, Func<NatsMsg<T>, Task> action)
    {
        if (sub == null)
            return Task.CompletedTask;
        return Task.Run(async () =>
        {
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync())
            {
                await action(natsMsg);
            }
        });
    }

    internal static Task Register(this NatsSub? sub, Action<NatsMsg> action)
    {
        if (sub == null)
            return Task.CompletedTask;
        return Task.Run(async () =>
        {
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync())
            {
                action(natsMsg);
            }
        });
    }

    internal static Task Register(this NatsSub? sub, Func<NatsMsg, Task> action)
    {
        if (sub == null)
            return Task.CompletedTask;
        return Task.Run(async () =>
        {
            await foreach (var natsMsg in sub.Msgs.ReadAllAsync())
            {
                await action(natsMsg);
            }
        });
    }
}

internal static class BinaryUtils
{
    public static string Dump(this in Memory<byte> memory) => Dump(memory.Span);

    public static string Dump(this in ReadOnlySpan<byte> span)
    {
        var sb = new StringBuilder();
        foreach (char b in span)
        {
            switch (b)
            {
            case >= ' ' and <= '~':
                sb.Append(b);
                break;
            case '\r':
                sb.Append('␍');
                break;
            case '\n':
                sb.Append('␊');
                break;
            default:
                sb.Append('.');
                break;
            }
        }

        return sb.ToString();
    }
}
