using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NATS.Client.Core.Tests;
using NATS.Client.Serializers.Json;
using NATS.Client.Services.Internal;
using NATS.Client.Services.Models;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Services.Tests;

public class ServicesSerializationTest
{
    private readonly ITestOutputHelper _output;

    public ServicesSerializationTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Service_info_and_stat_request_serialization()
    {
        await using var server = await NatsServerProcess.StartAsync();

        // Set serializer registry to use anything but a raw bytes (NatsMemory in this case) serializer
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, SerializerRegistry = NatsJsonSerializerRegistry.Default });

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

    [Fact]
    public async Task Service_message_serialization()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, SerializerRegistry = NatsJsonSerializerRegistry.Default });

        var svc = new NatsSvcContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var sync = Channel.CreateUnbounded<int>();
        var wait1 = new WaitSignal<NatsDeserializeException>();
        var wait2 = new WaitSignal<TestData>();

        await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);
        await s1.AddEndpointAsync<TestData>(
            name: "e1",
            handler: async m =>
            {
                if (m.Exception is NatsDeserializeException de)
                {
                    await m.ReplyAsync(1, cancellationToken: cancellationToken);
                    wait1.Pulse(de);
                    return;
                }

                if (m.Data is { Name: "sync" })
                {
                    await m.ReplyAsync(0, cancellationToken: cancellationToken);
                    sync.Writer.TryWrite(1);
                    return;
                }

                await m.ReplyAsync(0, cancellationToken: cancellationToken);
                wait2.Pulse(m.Data!);
            },
            cancellationToken: cancellationToken);

        await Retry.Until(
            "synced",
            () => sync.Reader.TryRead(out _),
            async () => await nats.RequestAsync<TestData, int>("e1", new TestData("sync"), cancellationToken: cancellationToken));

        var brokenJson = "{\"Name\": broken";
        var r1 = await nats.RequestAsync<string, int>("e1", brokenJson, requestSerializer: NatsUtf8PrimitivesSerializer<string>.Default, cancellationToken: cancellationToken);
        Assert.Equal(1, r1.Data);
        var de = await wait1;
        Assert.Equal(brokenJson, Encoding.ASCII.GetString(de.RawData));

        if (de.InnerException is JsonException je)
        {
            Assert.Contains("'b' is an invalid start of a value", je.Message);
        }
        else
        {
            Assert.Fail("Expected JsonException");
        }

        var r2 = await nats.RequestAsync<TestData, int>("e1", new TestData("abc"), cancellationToken: cancellationToken);
        Assert.Equal(0, r2.Data);
        var testData = await wait2;
    }

    private record TestData(string Name);
}
