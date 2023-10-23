using System.Text.Json.Nodes;
using NATS.Client.Core.Tests;
using NATS.Client.Services.Models;

namespace NATS.Client.Services.Tests;

public class ServicesTests
{
    private readonly ITestOutputHelper _output;

    public ServicesTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Add_service_listeners_ping_info_and_stats()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        var pingsTask = FindServices<PingResponse>(server, "$SRV.PING", 1, cancellationToken);
        var infosTask = FindServices<InfoResponse>(server, "$SRV.INFO", 1, cancellationToken);
        var statsTask = FindServices<StatsResponse>(server, "$SRV.STATS", 1, cancellationToken);

        var pings = await pingsTask;
        pings.ForEach(x => _output.WriteLine($"{x}"));
        Assert.Single(pings);
        Assert.Equal("s1", pings[0].Name);
        Assert.Equal("1.0.0", pings[0].Version);

        var infos = await infosTask;
        infos.ForEach(x => _output.WriteLine($"{x}"));
        Assert.Single(infos);
        Assert.Equal("s1", infos[0].Name);
        Assert.Equal("1.0.0", infos[0].Version);

        var stats = await statsTask;
        stats.ForEach(x => _output.WriteLine($"{x}"));
        Assert.Single(stats);
        Assert.Equal("s1", stats[0].Name);
        Assert.Equal("1.0.0", stats[0].Version);
    }

    [Fact]
    public async Task Add_end_point()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        await s1.AddEndPointAsync<int>(
            name: "e1",
            handler: async m =>
            {
                if (m.Data == 7)
                {
                    await m.ReplyErrorAsync(m.Data, $"Error{m.Data}", cancellationToken: cancellationToken);
                    return;
                }

                if (m.Data == 8)
                {
                    throw new NatsSvcEndPointException(m.Data, $"Error{m.Data}");
                }

                if (m.Data == 9)
                {
                    throw new Exception("this won't be exposed");
                }

                await m.ReplyAsync(m.Data * m.Data, cancellationToken: cancellationToken);
            },
            cancellationToken: cancellationToken);

        var info = (await FindServices<InfoResponse>(server, "$SRV.INFO", 1, cancellationToken)).First();
        Assert.Single(info.EndPoints);
        var endpointInfo = info.EndPoints.First();
        Assert.Equal("e1", endpointInfo.Name);

        for (int i = 0; i < 10; i++)
        {
            var response = await nats.RequestAsync<int, int>(endpointInfo.Subject, i, cancellationToken: cancellationToken);
            if (i is 7 or 8)
            {
                Assert.Equal($"{i}", response?.Headers?["Nats-Service-Error-Code"]);
                Assert.Equal($"Error{i}", response?.Headers?["Nats-Service-Error"]);
            }
            else if (i is 9)
            {
                Assert.Equal("999", response?.Headers?["Nats-Service-Error-Code"]);
                Assert.Equal("Handler error", response?.Headers?["Nats-Service-Error"]);
            }
            else
            {
                Assert.Equal(i * i, response?.Data);
                Assert.Null(response?.Headers);
            }
        }

        var stat = (await FindServices<StatsResponse>(server, "$SRV.STATS", 1, cancellationToken)).First();
        Assert.Single(stat.EndPoints);
        var endpointStats = stat.EndPoints.First();
        Assert.Equal("e1", endpointStats.Name);
        Assert.Equal(10, endpointStats.NumRequests);
        Assert.Equal(3, endpointStats.NumErrors);
        Assert.Equal("Handler error (999)", endpointStats.LastError);
        Assert.True(endpointStats.ProcessingTime > 0);
        Assert.True(endpointStats.AverageProcessingTime > 0);
    }

    [Fact]
    public async Task Add_groups_metadata_and_stats()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        await s1.AddEndPointAsync<int>(
            name: "baz",
            subject: "foo.baz",
            handler: m => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        await s1.AddEndPointAsync<int>(
            subject: "foo.bar1",
            handler: m => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        var grp1 = await s1.AddGroupAsync("grp1", cancellationToken: cancellationToken);

        await grp1.AddEndPointAsync<int>(
            name: "e1",
            handler: m => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        await grp1.AddEndPointAsync<int>(
            name: "e2",
            subject: "foo.bar2",
            handler: m => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        var grp2 = await s1.AddGroupAsync(string.Empty, queueGroup: "q_empty", cancellationToken: cancellationToken);

        await grp2.AddEndPointAsync<int>(
            name: "empty1",
            subject: "foo.empty1",
            handler: m => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        // Check that the endpoints are registered correctly
        {
            var info = (await FindServices<InfoResponse>(server, "$SRV.INFO.s1", 1, cancellationToken)).First();
            Assert.Equal(5, info.EndPoints.Count);
            var endpoints = info.EndPoints.ToList();

            Assert.Equal("foo.baz", info.EndPoints.First(e => e.Name == "baz").Subject);
            Assert.Equal("q", info.EndPoints.First(e => e.Name == "baz").QueueGroup);

            Assert.Equal("foo.bar1", info.EndPoints.First(e => e.Name == "foo.bar1").Subject);
            Assert.Equal("q", info.EndPoints.First(e => e.Name == "foo.bar1").QueueGroup);

            Assert.Equal("grp1.e1", info.EndPoints.First(e => e.Name == "grp1.e1").Subject);
            Assert.Equal("q", info.EndPoints.First(e => e.Name == "grp1.e1").QueueGroup);

            Assert.Equal("grp1.foo.bar2", info.EndPoints.First(e => e.Name == "grp1.e2").Subject);
            Assert.Equal("q", info.EndPoints.First(e => e.Name == "grp1.e2").QueueGroup);

            Assert.Equal("foo.empty1", info.EndPoints.First(e => e.Name == "empty1").Subject);
            Assert.Equal("q_empty", info.EndPoints.First(e => e.Name == "empty1").QueueGroup);
        }

        await using var s2 = await svc.AddServiceAsync(
            new NatsSvcConfig("s2", "2.0.0")
            {
                Description = "es-two",
                QueueGroup = "q2",
                Metadata = new Dictionary<string, string> { { "k1", "v1" }, { "k2", "v2" }, },
                StatsHandler = () => JsonNode.Parse("{\"stat-k1\":\"stat-v1\",\"stat-k2\":\"stat-v2\"}")!,
            },
            cancellationToken: cancellationToken);

        await s2.AddEndPointAsync<int>(
            name: "s2baz",
            subject: "s2foo.baz",
            handler: m => ValueTask.CompletedTask,
            metadata: new Dictionary<string, string> { { "ep-k1", "ep-v1" } },
            cancellationToken: cancellationToken);

        // Check default queue group and stats handler
        {
            var info = (await FindServices<InfoResponse>(server, "$SRV.INFO.s2", 1, cancellationToken)).First();
            Assert.Single(info.EndPoints);
            var epi = info.EndPoints.First();

            Assert.Equal("s2baz", epi.Name);
            Assert.Equal("s2foo.baz", epi.Subject);
            Assert.Equal("q2", epi.QueueGroup);
            Assert.Equal("ep-v1", epi.Metadata["ep-k1"]);

            var stat = (await FindServices<StatsResponse>(server, "$SRV.STATS.s2", 1, cancellationToken)).First();
            Assert.Equal("v1", stat.Metadata["k1"]);
            Assert.Equal("v2", stat.Metadata["k2"]);
            Assert.Single(stat.EndPoints);
            var eps = stat.EndPoints.First();
            Assert.Equal("stat-v1", eps.Data["stat-k1"]?.GetValue<string>());
            Assert.Equal("stat-v2", eps.Data["stat-k2"]?.GetValue<string>());
        }
    }

    private static async Task<List<T>> FindServices<T>(NatsServer server, string subject, int limit, CancellationToken ct)
    {
        await using var nats = server.CreateClientConnection();
        var replyOpts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) };
        var responses = new List<T>();

        var count = 0;
        await foreach (var msg in nats.RequestManyAsync<object?, T>(subject, null, replyOpts: replyOpts, cancellationToken: ct).ConfigureAwait(false))
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
