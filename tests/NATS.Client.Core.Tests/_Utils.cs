using System.Diagnostics;

namespace NATS.Client.Core.Tests;

public static class Retry
{
    public static async Task Until(string reason, Func<bool> condition, Func<Task>? action = null, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (action != null)
                await action();
            if (condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Took too long ({timeout}) waiting until {reason}");
    }
}
