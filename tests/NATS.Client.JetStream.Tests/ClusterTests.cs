using System.Net;
using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ClusterTests
{
    private readonly ITestOutputHelper _output;

    public ClusterTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Form_a_local_cluster()
    {
        await using var cluster = new NatsCluster(new NullOutputHelper(), TransportType.Tcp, (i, b) => b.WithServerName($"n{i}").UseJetStream());
        await using var nats = cluster.Server1.CreateClientConnection();

        await nats.PingAsync();

        var urls = nats.ServerInfo!.ClientConnectUrls!.ToList();

        Assert.Equal(3, urls.Count);

        foreach (var url in urls)
        {
            _output.WriteLine(url);
            Assert.Matches(@"127\.0\.0\.1", url);
        }
    }

    [Fact]
    public async Task Seed_urls_on_retry()
    {
        await using var cluster1 = new NatsCluster(
            new NullOutputHelper(),
            TransportType.Tcp,
            (i, b) => b.WithServerName($"c1n{i}"));

        await using var cluster2 = new NatsCluster(
            new NullOutputHelper(),
            TransportType.Tcp,
            (i, b) => b.WithServerName($"c2n{i}"));

        var url1 = cluster1.Server1.ClientUrl;
        var url2 = cluster2.Server1.ClientUrl;

        await using var nats = new NatsConnection(new NatsOpts
        {
            NoRandomize = true,
            Url = $"{url1},{url2}",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        HashSet<string> connectedUrls = new();
        nats.ConnectionOpened += (_, _) =>
        {
            lock (connectedUrls) connectedUrls.Add(nats.ServerInfo!.Name);
            return default;
        };

        await nats.PingAsync(cts.Token);

        var allServers = cluster1.Servers.Concat(cluster2.Servers).ToList();
        var currentCount = 0;
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();

            var changed = false;
            lock (connectedUrls)
            {
                if (connectedUrls.Count != currentCount)
                {
                    currentCount = connectedUrls.Count;
                    changed = true;
                }
            }

            if (changed)
            {
                await allServers[currentCount - 1].StopAsync();
                lock (connectedUrls)
                {
                    _output.WriteLine($"Connected to another server ({currentCount})");
                    foreach (var allUrl in connectedUrls)
                    {
                        _output.WriteLine($"url: {allUrl}");
                    }
                }
            }

            if (currentCount == 6) break;

            await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }

        lock (connectedUrls)
        {
            Assert.Equal(
                connectedUrls.OrderBy(x => x),
                ["c1n1", "c1n2", "c1n3", "c2n1", "c2n2", "c2n3"]);
        }
    }
}
