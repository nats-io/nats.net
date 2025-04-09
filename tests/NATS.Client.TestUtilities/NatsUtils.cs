using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NATS.Client.Core;

namespace NATS.Client.TestUtilities2;

public static class NatsUtils
{
    public static async Task ConnectRetryAsync(this INatsClient client, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        Exception? exception = null;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                await client.ConnectAsync();
                exception = null;
                break;
            }
            catch (Exception e)
            {
                exception = e;
                await Task.Delay(1000);
            }
        }

        if (exception != null)
            throw exception;
    }
}
