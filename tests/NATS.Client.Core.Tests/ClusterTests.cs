using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

public class ClusterTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seed_urls_on_retry(bool userAuthInUrl)
    {
        await using var cluster1 = new NatsCluster(
            new NullOutputHelper(),
            TransportType.Tcp,
            (i, b) =>
            {
                b.WithServerName($"c1n{i}");
                if (userAuthInUrl)
                {
                    b.AddServerConfig("resources/configs/auth/password.conf");
                    b.WithClientUrlAuthentication("a", "b");
                }
            },
            userAuthInUrl);
        await cluster1.StartAsync();

        await using var cluster2 = new NatsCluster(
            new NullOutputHelper(),
            TransportType.Tcp,
            (i, b) =>
            {
                b.WithServerName($"c2n{i}");
                if (userAuthInUrl)
                {
                    b.AddServerConfig("resources/configs/auth/password.conf");
                    b.WithClientUrlAuthentication("a", "b");
                }
            },
            userAuthInUrl);
        await cluster2.StartAsync();

        // Use the first node from each cluster as the seed
        // so that we can confirm seeds are used on retry
        var url1 = userAuthInUrl ? cluster1.Server1.ClientUrlWithAuth : cluster1.Server1.ClientUrl;
        var url2 = userAuthInUrl ? cluster2.Server1.ClientUrlWithAuth : cluster2.Server1.ClientUrl;

        await using var nats = new NatsConnection(new NatsOpts
        {
            NoRandomize = true,
            Url = $"{url1},{url2}",
        });
        await nats.ConnectRetryAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        HashSet<string> connectedUrls = new();
        nats.ConnectionOpened += (_, _) =>
        {
            lock (connectedUrls)
                connectedUrls.Add(nats.ServerInfo!.Name);
            return default;
        };

        await nats.PingAsync(cts.Token);

        var currentConnectedUrlCount = 0;
        var firstClusterStopped = false;
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();

            var changed = false;
            lock (connectedUrls)
            {
                if (connectedUrls.Count != currentConnectedUrlCount)
                {
                    currentConnectedUrlCount = connectedUrls.Count;
                    changed = true;
                }
            }

            if (changed)
            {
                if (!firstClusterStopped)
                {
                    await cluster1.Server1.StopAsync();
                    await cluster1.Server2.StopAsync();
                    await cluster1.Server3.StopAsync();
                    firstClusterStopped = true;
                }

                lock (connectedUrls)
                {
                    output.WriteLine($"Connected to another server ({currentConnectedUrlCount})");
                    foreach (var allUrl in connectedUrls)
                    {
                        output.WriteLine($"url: {allUrl}");
                    }

                    if (connectedUrls.Select(x => x.Substring(0, 2)).Distinct().Count() == 2)
                        break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }

        lock (connectedUrls)
        {
            Assert.Equal(
                connectedUrls.Select(x => x.Substring(0, 2)).Distinct().OrderBy(x => x),
                ["c1", "c2"]);
        }
    }
}
