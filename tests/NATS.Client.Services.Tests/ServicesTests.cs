using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NATS.Client.Core.Tests;
using NATS.Client.Services.Internal;
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

        var pingsTask = nats.FindServicesAsync("$SRV.PING", 1, NatsSrvJsonSerializer<PingResponse>.Default, cancellationToken);
        var infosTask = nats.FindServicesAsync("$SRV.INFO", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken);
        var statsTask = nats.FindServicesAsync("$SRV.STATS", 1, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken);

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

        await s1.AddEndpointAsync<int>(
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
                    throw new NatsSvcEndpointException(m.Data, $"Error{m.Data}");
                }

                if (m.Data == 9)
                {
                    throw new Exception("this won't be exposed");
                }

                await m.ReplyAsync(m.Data * m.Data, cancellationToken: cancellationToken);
            },
            cancellationToken: cancellationToken);

        var info1 = (await nats.FindServicesAsync("$SRV.INFO", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken)).First();
        var info2 = s1.GetInfo();
        foreach (var info in new[] { info1, info2 })
        {
            Assert.Single(info.Endpoints);
            var endpointInfo1 = info.Endpoints.First();
            Assert.Equal("e1", endpointInfo1.Name);
        }

        var endpointInfo = info1.Endpoints.First();

        for (var i = 0; i < 10; i++)
        {
            var response = await nats.RequestAsync<int, int>(endpointInfo.Subject, i, cancellationToken: cancellationToken);
            if (i is 7 or 8)
            {
                Assert.Equal($"{i}", response.Headers?["Nats-Service-Error-Code"]);
                Assert.Equal($"Error{i}", response.Headers?["Nats-Service-Error"]);
            }
            else if (i is 9)
            {
                Assert.Equal("999", response.Headers?["Nats-Service-Error-Code"]);
                Assert.Equal("Handler error", response.Headers?["Nats-Service-Error"]);
            }
            else
            {
                Assert.Equal(i * i, response.Data);
                Assert.Null(response.Headers);
            }
        }

        var stat1 = (await nats.FindServicesAsync("$SRV.STATS", 1, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken)).First();
        var stat2 = s1.GetStats();
        foreach (var stat in new[] { stat1, stat2 })
        {
            Assert.Single(stat.Endpoints);
            var endpointStats = stat.Endpoints.First();
            Assert.Equal("e1", endpointStats.Name);
            Assert.Equal(10, endpointStats.NumRequests);
            Assert.Equal(3, endpointStats.NumErrors);
            Assert.Equal("999:Handler error", endpointStats.LastError);
            Assert.True(endpointStats.ProcessingTime > 0);
            Assert.True(endpointStats.AverageProcessingTime > 0);
        }
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

        await s1.AddEndpointAsync<int>(
            name: "baz",
            subject: "foo.baz",
            handler: _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        await s1.AddEndpointAsync<int>(
            subject: "foo.bar1",
            handler: _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        var grp1 = await s1.AddGroupAsync("grp1", cancellationToken: cancellationToken);

        await grp1.AddEndpointAsync<int>(
            name: "e1",
            handler: _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        await grp1.AddEndpointAsync<int>(
            name: "e2",
            subject: "foo.bar2",
            handler: _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        var grp2 = await s1.AddGroupAsync(string.Empty, queueGroup: "q_empty", cancellationToken: cancellationToken);

        await grp2.AddEndpointAsync<int>(
            name: "empty1",
            subject: "foo.empty1",
            handler: _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        // Check that the endpoints are registered correctly
        {
            var info = (await nats.FindServicesAsync("$SRV.INFO.s1", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken)).First();
            Assert.Equal(5, info.Endpoints.Count);

            Assert.Equal("foo.baz", info.Endpoints.First(e => e.Name == "baz").Subject);
            Assert.Equal("q", info.Endpoints.First(e => e.Name == "baz").QueueGroup);

            Assert.Equal("foo.bar1", info.Endpoints.First(e => e.Name == "foo-bar1").Subject);
            Assert.Equal("q", info.Endpoints.First(e => e.Name == "foo-bar1").QueueGroup);

            Assert.Equal("grp1.e1", info.Endpoints.First(e => e.Name == "e1").Subject);
            Assert.Equal("q", info.Endpoints.First(e => e.Name == "e1").QueueGroup);

            Assert.Equal("grp1.foo.bar2", info.Endpoints.First(e => e.Name == "e2").Subject);
            Assert.Equal("q", info.Endpoints.First(e => e.Name == "e2").QueueGroup);

            Assert.Equal("foo.empty1", info.Endpoints.First(e => e.Name == "empty1").Subject);
            Assert.Equal("q_empty", info.Endpoints.First(e => e.Name == "empty1").QueueGroup);
        }

        await using var s2 = await svc.AddServiceAsync(
            new NatsSvcConfig("s2", "2.0.0")
            {
                Description = "es-two",
                QueueGroup = "q2",
                Metadata = new Dictionary<string, string> { { "k1", "v1" }, { "k2", "v2" }, },
                StatsHandler = ep => JsonNode.Parse($"{{\"stat-k1\":\"stat-v1\",\"stat-k2\":\"stat-v2\",\"ep_name\": \"{ep.Name}\"}}")!,
            },
            cancellationToken: cancellationToken);

        await s2.AddEndpointAsync<int>(
            name: "s2baz",
            subject: "s2foo.baz",
            handler: _ => ValueTask.CompletedTask,
            metadata: new Dictionary<string, string> { { "ep-k1", "ep-v1" } },
            cancellationToken: cancellationToken);

        // Check default queue group and stats handler
        {
            var info = (await nats.FindServicesAsync("$SRV.INFO.s2", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken)).First();
            Assert.Single(info.Endpoints);
            var epi = info.Endpoints.First();

            Assert.Equal("s2baz", epi.Name);
            Assert.Equal("s2foo.baz", epi.Subject);
            Assert.Equal("q2", epi.QueueGroup);
            Assert.Equal("ep-v1", epi.Metadata["ep-k1"]);

            var stat = (await nats.FindServicesAsync("$SRV.STATS.s2", 1, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken)).First();
            Assert.Equal("v1", stat.Metadata["k1"]);
            Assert.Equal("v2", stat.Metadata["k2"]);
            Assert.Single(stat.Endpoints);
            var eps = stat.Endpoints.First();
            Assert.Equal("stat-v1", eps.Data["stat-k1"]?.GetValue<string>());
            Assert.Equal("stat-v2", eps.Data["stat-k2"]?.GetValue<string>());
            Assert.Equal("s2baz", eps.Data["ep_name"]?.GetValue<string>());
        }
    }

    [Fact]
    public async Task Add_multiple_service_listeners_ping_info_and_stats()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);
        await using var s2 = await svc.AddServiceAsync("s2", "2.0.0", cancellationToken: cancellationToken);

        var pingsTask = nats.FindServicesAsync("$SRV.PING", 2, NatsSrvJsonSerializer<PingResponse>.Default, cancellationToken);
        var infosTask = nats.FindServicesAsync("$SRV.INFO", 2, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken);
        var statsTask = nats.FindServicesAsync("$SRV.STATS", 2, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken);

        var pings = await pingsTask;
        Assert.Equal(2, pings.Count);
        Assert.Equal("1.0.0", pings.First(s => s.Name == "s1").Version);
        Assert.Equal("2.0.0", pings.First(s => s.Name == "s2").Version);

        var infos = await infosTask;
        Assert.Equal(2, infos.Count);
        Assert.Equal("1.0.0", infos.First(s => s.Name == "s1").Version);
        Assert.Equal("2.0.0", infos.First(s => s.Name == "s2").Version);

        var stats = await statsTask;
        Assert.Equal(2, stats.Count);
        Assert.Equal("1.0.0", stats.First(s => s.Name == "s1").Version);
        Assert.Equal("2.0.0", stats.First(s => s.Name == "s2").Version);
    }

    [Fact]
    public async Task Pass_headers_to_request_and_in_response()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        await s1.AddEndpointAsync<int>(
            name: "e1",
            handler: async m =>
            {
                if (m.Headers != null)
                {
                    var headers = m.Headers;
                    if (headers.TryGetValue("foo", out var foo))
                    {
                        if (foo != "bar")
                        {
                            await m.ReplyErrorAsync(m.Data, "Expected 'foo' = 'bar' header", cancellationToken: cancellationToken);
                            return;
                        }

                        await m.ReplyAsync(m.Data, headers: new NatsHeaders { { "bar", "baz" } }, cancellationToken: cancellationToken);
                        return;
                    }
                }

                await m.ReplyErrorAsync(m.Data, "Missing 'foo' header", cancellationToken: cancellationToken);
            },
            cancellationToken: cancellationToken);

        // With headers
        var headers = new NatsHeaders { { "foo", "bar" } };
        var response = await nats.RequestAsync<int, int>("e1", 999, headers, cancellationToken: cancellationToken);
        Assert.Equal(999, response.Data);
        Assert.Equal("baz", response.Headers?["bar"]);

        // With headers, but not the expected one.
        headers = new NatsHeaders
        {
            { "not-the-expected", "4711" },
            { "also-not-the-expected", "4242" },
        };
        response = await nats.RequestAsync<int, int>("e1", 999, headers, cancellationToken: cancellationToken);
        Assert.Equal("999", response.Headers?["Nats-Service-Error-Code"]);
        Assert.Equal("Missing 'foo' header", response.Headers?["Nats-Service-Error"]);

        // No headers.
        response = await nats.RequestAsync<int, int>("e1", 999, cancellationToken: cancellationToken);
        Assert.Equal("999", response.Headers?["Nats-Service-Error-Code"]);
        Assert.Equal("Missing 'foo' header", response.Headers?["Nats-Service-Error"]);
    }

    [Fact]
    public async Task Service_started_time()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        await s1.AddEndpointAsync<int>(
            name: "e1",
            handler: async m =>
            {
                await m.ReplyAsync(m.Data, cancellationToken: cancellationToken);
            },
            cancellationToken: cancellationToken);

        var stats = s1.GetStats();

        // Match: 2021-09-01T12:34:56.1234567Z
        var formatRegex = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}.\d{7}Z$");
        Assert.Matches(formatRegex, stats.Started);
    }

    [Fact]
    public async Task Service_ids_unique()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        var svc = new NatsSvcContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        List<string> ids = new();
        for (var i = 0; i < 100; i++)
        {
            await using var s = await svc.AddServiceAsync($"s{i}", "1.0.0", cancellationToken: cancellationToken);
            ids.Add(s.GetInfo().Id);
        }

        ids.Sort();

        HashSet<string> uniqueIds = new();
        foreach (var id in ids)
        {
            if (!uniqueIds.Add(id))
            {
                _output.WriteLine($"Duplicate ID: {id}");
            }

            _output.WriteLine($"{id.Substring(0, 12)} {id.Substring(12, 10)}");
        }

        Assert.Equal(ids.Count, uniqueIds.Count);
    }
}
