using System.Text;
using System.Text.Json.Nodes;
using NATS.Client.Core.Tests;
using NATS.Client.ObjectStore.Models;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.ObjectStore.Tests;

public class CompatTest
{
    [Fact]
    public async Task Headers_serialization()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        var objMeta = new ObjectMetadata()
        {
            Name = "o1",
            Bucket = "b1",
            Headers = new Dictionary<string, string[]> { { "a", new[] { "1" } } },
        };

        // Verify PUT
        {
            await using var stream = new MemoryStream("Hello, World!"u8.ToArray());
            await store.PutAsync(objMeta, stream, cancellationToken: cancellationToken);

            // nats obj info b1 o1
            var request = """{"last_by_subj":"$O.b1.M.bzE="}""";
            var response = JsonNode.Parse((await nats.RequestAsync<string, string>(
                subject: "$JS.API.STREAM.MSG.GET.OBJ_b1",
                data: request,
                cancellationToken: cancellationToken)).Data!);

            var data = JsonNode.Parse(Encoding.ASCII.GetString(Convert.FromBase64String(response!["message"]!["data"]!.GetValue<string>())));
            data!["headers"]!["a"]!.Should().BeOfType<JsonArray>();

            var jsonArray = data["headers"]!["a"]!.AsArray();
            jsonArray.Count.Should().Be(1);
            jsonArray[0]!.GetValue<string>().Should().Be("1");
        }

        // Verify GET
        {
            // "Hello, World!" > nats obj put b1 --name="o2" -H "b:2"
            await nats.RequestAsync<string, string>(
                subject: "$O.b1.C.zU8HBwqBREAgzRNG2J58Tu",
                data: "Hello, World!",
                cancellationToken: cancellationToken);

            await nats.RequestAsync<string, string>(
                subject: "$O.b1.M.bzI=",
                headers: new NatsHeaders { { "nNats-Rollup", "sub" } },
                data: """{"name":"o2","headers":{"b":["2"]},"options":{"max_chunk_size":131072},"bucket":"b1","nuid":"zU8HBwqBREAgzRNG2J58Tu","size":15,"mtime":"0001-01-01T00:00:00Z","chunks":1,"digest":"SHA-256=krdyOAo_jiepPlfm3uymwB2gf1qtzni7L7sg3hCmaSU="}""",
                cancellationToken: cancellationToken);

            var info = await store.GetInfoAsync("o2", cancellationToken: cancellationToken);
            info.Headers!.Count.Should().Be(1);
            info.Headers!["b"].Should().BeEquivalentTo(new[] { "2" });
        }
    }
}
