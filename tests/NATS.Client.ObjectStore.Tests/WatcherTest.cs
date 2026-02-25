using NATS.Client.Core.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.ObjectStore.Tests;

public class WatcherTest
{
    [Fact]
    public async Task Watcher_test()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var store = await ob.CreateObjectStoreAsync("b1", cancellationToken);

        await store.PutAsync("k0", [0], cancellationToken);

        var signal = new WaitSignal();

        var watcher = Task.Run(
            async () =>
            {
                var count = 0;
                await foreach (var info in store.WatchAsync(cancellationToken: cancellationToken))
                {
                    count++;
                    signal.Pulse();
                    Assert.Equal((ulong)count, info.Size);
                    if (count == 3)
                        break;
                }
            },
            cancellationToken);

        await signal;

        await store.PutAsync("k1", new byte[] { 0, 1 }, cancellationToken);
        await store.PutAsync("k1", new byte[] { 0, 1, 3 }, cancellationToken);

        await watcher;
    }
}
