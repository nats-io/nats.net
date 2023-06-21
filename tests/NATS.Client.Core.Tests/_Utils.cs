using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

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
