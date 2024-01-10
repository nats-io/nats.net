using System.Buffers;
using NATS.Client.Core.Tests;
using NATS.Client.Serializers.Json;
using NATS.Client.Services.Internal;
using NATS.Client.Services.Models;

namespace NATS.Client.Services.Tests;

public class ServicesSerializationTest
{
    private readonly ITestOutputHelper _output;

    public ServicesSerializationTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Service_info_and_stat_request_serialization()
    {
        await using var server = NatsServer.Start();

        // Set serializer registry to use anything but a raw bytes (NatsMemory in this case) serializer
        await using var nats = server.CreateClientConnection(new NatsOpts { SerializerRegistry = NatsJsonSerializerRegistry.Default });

        var svc = new NatsSvcContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

        var infosTask = nats.FindServicesAsync("$SRV.INFO", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken);
        var statsTask = nats.FindServicesAsync("$SRV.STATS", 1, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken);

        var infos = await infosTask;
        Assert.Single(infos);
        Assert.Equal("s1", infos[0].Name);
        Assert.Equal("1.0.0", infos[0].Version);

        var stats = await statsTask;
        Assert.Single(stats);
        Assert.Equal("s1", stats[0].Name);
        Assert.Equal("1.0.0", stats[0].Version);
    }
}
