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
}
