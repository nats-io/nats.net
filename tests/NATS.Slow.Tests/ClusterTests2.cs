using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

public class ClusterTests2
{
    private readonly ITestOutputHelper _output;

    public ClusterTests2(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Form_a_local_cluster()
    {
        await using var cluster = new NatsCluster(new NullOutputHelper(), TransportType.Tcp, (i, b) => b.WithServerName($"n{i}").UseJetStream());
        await cluster.StartAsync();
        await using var nats = await cluster.Server1.CreateClientConnectionAsync();

        await nats.ConnectRetryAsync();

        var urls = nats.ServerInfo!.ClientConnectUrls!.ToList();

        Assert.Equal(3, urls.Count);

        foreach (var url in urls)
        {
            _output.WriteLine(url);
            Assert.Matches(@"127\.0\.0\.1", url);
        }
    }
}
