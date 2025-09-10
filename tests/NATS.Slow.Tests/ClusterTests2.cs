using System.Diagnostics;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;
using NATS.Net;

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

    [Fact]
    public async Task Check_JetStream_cluster_related_fields()
    {
        var started = DateTimeOffset.Now;
        await using var cluster = new NatsCluster(
            new NullOutputHelper(),
            TransportType.Tcp,
            (i, b) =>
            {
                b.WithServerName($"n{i}");
                b.WithClusterName("C1");
                b.UseJetStream();
            });
        await cluster.StartAsync();
        await using var nats = await cluster.Server1.CreateClientConnectionAsync();
        await nats.ConnectRetryAsync();

        // Wait for the cluster to form
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await nats.RequestAsync<string>("$JS.API.INFO");
                break;
            }
            catch (NatsException)
            {
            }
        }

        var js = nats.CreateJetStreamContext();
        INatsJSStream s1 = null!;

        // Wait until stream can be created
        for (var i = 0; i < 10; i++)
        {
            try
            {
                s1 = await js.CreateStreamAsync(new StreamConfig
                {
                    Name = "s1",
                    Subjects = ["s1"],
                    NumReplicas = 3,
                });
                break;
            }
            catch (NatsException)
            {
            }
        }

        Assert.Equal("C1", s1.Info.Cluster!.Name);
        Assert.True(s1.Info.Cluster.Replicas!.Count > 0);
        Assert.NotNull(s1.Info.Cluster.Leader);
        Assert.Matches("n[123]", s1.Info.Cluster.Leader);
        Assert.NotNull(s1.Info.Cluster.RaftGroup);
        Assert.True(s1.Info.Cluster.RaftGroup.Length > 0);

        if (nats.ServerInfo.VersionMajorMinorIsGreaterThenOrEqualTo(2, 12))
        {
            var tolerance = TimeSpan.FromSeconds(10);
            Assert.True(s1.Info.Cluster.LeaderSince > started - tolerance);
            Assert.True(s1.Info.Cluster.LeaderSince < DateTimeOffset.Now + tolerance);
            Assert.Equal("$SYS", s1.Info.Cluster.TrafficAccount);
            Assert.True(s1.Info.Cluster.SystemAccount);
        }
    }
}
