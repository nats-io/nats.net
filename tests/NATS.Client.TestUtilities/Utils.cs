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

    public static async Task Until(string reason, Func<Task<bool>> condition, Func<Task>? action = null, TimeSpan? timeout = null, TimeSpan? retryDelay = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var delay1 = retryDelay ?? TimeSpan.FromSeconds(.1);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (action != null)
                await action();
            if (await condition())
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

public static class NatsMsgTestUtils
{
    public static Task Register<T>(this INatsSub<T>? sub, Action<NatsMsg<T>> action)
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

    public static Task Register<T>(this INatsSub<T>? sub, Func<NatsMsg<T>, Task> action)
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

public static class BinaryUtils
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

public static class ServiceUtils
{
    public static async Task<List<T>> FindServicesAsync<T>(this NatsConnection nats, string subject, int limit, INatsDeserialize<T> serializer, CancellationToken ct)
    {
        var replyOpts = new NatsSubOpts
        {
            Timeout = TimeSpan.FromSeconds(2),
        };
        var responses = new List<T>();

        await Retry.Until("service is found", async () =>
        {
            var count = 0;

            // nats cli sends an empty JSON object '{}' as the request payload so we do the same here
            await foreach (var msg in nats.RequestManyAsync<string, T>(subject, "{}", replySerializer: serializer, replyOpts: replyOpts, cancellationToken: ct).ConfigureAwait(false))
            {
                if (++count == limit)
                    break;
            }

            return count == limit;
        });

        var count = 0;
        await foreach (var msg in nats.RequestManyAsync<string, T>(subject, "{}", replySerializer: serializer, replyOpts: replyOpts, cancellationToken: ct).ConfigureAwait(false))
        {
            responses.Add(msg.Data!);
            if (++count == limit)
                break;
        }

        if (count != limit)
        {
            throw new Exception($"Find service error: Expected {limit} responses but got {count}");
        }

        return responses;
    }
}
