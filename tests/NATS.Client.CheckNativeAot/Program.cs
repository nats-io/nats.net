using System.Text;
using System.Text.Json.Nodes;
using NATS.Client.Core;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NATS.Client.ObjectStore;
using NATS.Client.ObjectStore.Models;
using NATS.Client.Services;
using NATS.Client.Services.Internal;
using NATS.Client.Services.Models;

Log("Starting...");

await RequestReplyTests();
await JetStreamTests();
await KVTests();
await ObjectStoreTests();
await ServicesTests();
await ServicesTests2();

Log("Bye");

void Log(string message)
{
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
}

async Task RequestReplyTests()
{
    Log("Request reply tests...");

    await using var server = NatsServer.Start();
    await using var nats = server.CreateClientConnection();

    var sub = await nats.SubscribeAsync<int>("foo");
    var reg = sub.Register(async msg =>
    {
        await msg.ReplyAsync(msg.Data * 2);
        await msg.ReplyAsync(msg.Data * 3);
        await msg.ReplyAsync(msg.Data * 4);
        await msg.ReplyAsync<int?>(null); // sentinel
    });

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var results = new[] { 2, 3, 4 };
    var count = 0;
    await foreach (var msg in nats.RequestManyAsync<int, int?>("foo", 1, cancellationToken: cts.Token))
    {
        Assert.Equal(results[count++], msg.Data);
    }

    Assert.Equal(3, count);

    await sub.DisposeAsync();
    await reg;

    Log("OK");
}

async Task JetStreamTests()
{
    Log("JetStream tests...");

    await using var server = NatsServer.StartJS();
    var nats = server.CreateClientConnection();

    // Happy user
    {
        var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var js = new NatsJSContext(nats);
        // Create stream
        var stream = await js.CreateStreamAsync(
            request: new StreamConfiguration { Name = "events", Subjects = new[] { "events.*" }, },
            cancellationToken: cts1.Token);
        Assert.Equal("events", stream.Info.Config.Name);
        // Create consumer
        var consumer = await js.CreateConsumerAsync(
            new ConsumerCreateRequest
            {
                StreamName = "events",
                Config = new ConsumerConfiguration
                {
                    Name = "consumer1",
                    DurableName = "consumer1",
                    // Turn on ACK so we can test them below
                    AckPolicy = ConsumerConfigurationAckPolicy.@explicit,
                },
            },
            cts1.Token);
        Assert.Equal("events", consumer.Info.StreamName);
        Assert.Equal("consumer1", consumer.Info.Config.Name);
        // Publish
        var ack = await js.PublishAsync("events.foo", new TestData { Test = 1 }, opts: new NatsPubOpts { Serializer = TestDataJsonSerializer.Default },  cancellationToken: cts1.Token);
        Assert.Null(ack.Error);
        Assert.Equal("events", ack.Stream);
        Assert.Equal(1, ack.Seq);
        Assert.False(ack.Duplicate);
        // Message ID
        ack = await js.PublishAsync(
            "events.foo",
            new TestData { Test = 2 },
            opts: new NatsPubOpts { Serializer = TestDataJsonSerializer.Default },
            headers: new NatsHeaders { { "Nats-Msg-Id", "test2" } },
            cancellationToken: cts1.Token);
        Assert.Null(ack.Error);
        Assert.Equal("events", ack.Stream);
        Assert.Equal(2, ack.Seq);
        Assert.False(ack.Duplicate);
        // Duplicate
        ack = await js.PublishAsync(
            "events.foo",
            new TestData { Test = 2 },
            opts: new NatsPubOpts { Serializer = TestDataJsonSerializer.Default },
            headers: new NatsHeaders { { "Nats-Msg-Id", "test2" } },
            cancellationToken: cts1.Token);
        Assert.Null(ack.Error);
        Assert.Equal("events", ack.Stream);
        Assert.Equal(2, ack.Seq);
        Assert.True(ack.Duplicate);
        // Consume
        var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var messages = new List<NatsJSMsg<TestData?>>();
        var cc = await consumer.ConsumeAsync<TestData>(
            new NatsJSConsumeOpts { MaxMsgs = 100, Serializer = TestDataJsonSerializer.Default },
            cancellationToken: cts2.Token);
        await foreach (var msg in cc.Msgs.ReadAllAsync(cts2.Token))
        {
            messages.Add(msg);
            // Only ACK one message so we can consume again
            if (messages.Count == 1)
            {
                await msg.AckAsync(new AckOpts(WaitUntilSent: true), cancellationToken: cts2.Token);
            }
            if (messages.Count == 2)
            {
                break;
            }
        }
        Assert.Equal(2, messages.Count);
        Assert.Equal("events.foo", messages[0].Subject);
        Assert.Equal("events.foo", messages[1].Subject);
    }
    // Handle errors
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var js = new NatsJSContext(nats);
        var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
        {
            await js.CreateStreamAsync(
                request: new StreamConfiguration
                {
                    Name = "events2",
                    Subjects = new[] { "events.*" },
                },
                cancellationToken: cts.Token);
        });
        Assert.Equal(400, exception.Error.Code);
        // subjects overlap with an existing stream
        Assert.Equal(10065, exception.Error.ErrCode);
    }
    // Delete stream
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var js = new NatsJSContext(nats);
        // Success
        await js.DeleteStreamAsync("events", cts.Token);
        // Error
        var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
        {
            await js.DeleteStreamAsync("events2", cts.Token);
        });
        Assert.Equal(404, exception.Error.Code);
        // stream not found
        Assert.Equal(10059, exception.Error.ErrCode);
    }

    Log("OK");
}

async Task KVTests()
{
    Log("KV tests...");

    await using var server = NatsServer.StartJS();
    await using var nats = server.CreateClientConnection();

    var js = new NatsJSContext(nats);
    var kv = new NatsKVContext(js);

    var store = await kv.CreateStoreAsync("b1");

    await store.PutAsync("k1", "v1");

    var entry = await store.GetEntryAsync<string>("k1");
    Assert.Equal("v1", entry.Value);

    Log("OK");
}

async Task ObjectStoreTests()
{
    Log("Object store tests...");

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var cancellationToken = cts.Token;

    await using var server = NatsServer.StartJS();
    await using var nats = server.CreateClientConnection();
    var js = new NatsJSContext(nats);
    var ob = new NatsObjContext(js);

    var store = await ob.CreateObjectStore(new NatsObjConfig("b1"), cancellationToken);

    var stringBuilder = new StringBuilder();
    for (var i = 0; i < 9; i++)
    {
        stringBuilder.Append($"{i:D2}-4567890");
    }

    var buffer90 = stringBuilder.ToString();

    // square buffer: all chunks are the same size
    {
        var meta = new ObjectMetadata { Name = "k1", Options = new MetaDataOptions { MaxChunkSize = 10 }, };
        var buffer = Encoding.ASCII.GetBytes(buffer90);
        var stream = new MemoryStream(buffer);
        await store.PutAsync(meta, stream, cancellationToken: cancellationToken);
    }

    {
        var memoryStream = new MemoryStream();
        await store.GetAsync("k1", memoryStream, cancellationToken: cancellationToken);
        await memoryStream.FlushAsync(cancellationToken);
        var buffer = memoryStream.ToArray();
        Assert.Equal(buffer90, Encoding.ASCII.GetString(buffer));
    }

    // buffer with smaller last chunk
    {
        var meta = new ObjectMetadata { Name = "k2", Options = new MetaDataOptions { MaxChunkSize = 10 }, };
        var buffer = Encoding.ASCII.GetBytes(buffer90 + "09-45");
        var stream = new MemoryStream(buffer);
        await store.PutAsync(meta, stream, cancellationToken: cancellationToken);
    }

    {
        var memoryStream = new MemoryStream();
        await store.GetAsync("k2", memoryStream, cancellationToken: cancellationToken);
        await memoryStream.FlushAsync(cancellationToken);
        var buffer = memoryStream.ToArray();
        Assert.Equal(buffer90 + "09-45", Encoding.ASCII.GetString(buffer));
    }

    Log("OK");
}

async Task ServicesTests()
{
    Log("Services tests...");

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var cancellationToken = cts.Token;

    await using var server = NatsServer.Start();
    await using var nats = server.CreateClientConnection();
    var svc = new NatsSvcContext(nats);

    await using var s1 = await svc.AddServiceAsync("s1", "1.0.0", cancellationToken: cancellationToken);

    await s1.AddEndpointAsync<int>(
        name: "baz",
        subject: "foo.baz",
        handler: m => ValueTask.CompletedTask,
        cancellationToken: cancellationToken);

    await s1.AddEndpointAsync<int>(
        subject: "foo.bar1",
        handler: m => ValueTask.CompletedTask,
        cancellationToken: cancellationToken);

    var grp1 = await s1.AddGroupAsync("grp1", cancellationToken: cancellationToken);

    await grp1.AddEndpointAsync<int>(
        name: "e1",
        handler: m => ValueTask.CompletedTask,
        cancellationToken: cancellationToken);

    await grp1.AddEndpointAsync<int>(
        name: "e2",
        subject: "foo.bar2",
        handler: m => ValueTask.CompletedTask,
        cancellationToken: cancellationToken);

    var grp2 = await s1.AddGroupAsync(string.Empty, queueGroup: "q_empty", cancellationToken: cancellationToken);

    await grp2.AddEndpointAsync<int>(
        name: "empty1",
        subject: "foo.empty1",
        handler: m => ValueTask.CompletedTask,
        cancellationToken: cancellationToken);

    // Check that the endpoints are registered correctly
    {
        var info = (await FindServices<InfoResponse>(nats, "$SRV.INFO.s1", 1, cancellationToken)).First();
        Assert.Equal(5, info.Endpoints.Count);
        var endpoints = info.Endpoints.ToList();

        Assert.Equal("foo.baz", info.Endpoints.First(e => e.Name == "baz").Subject);
        Assert.Equal("q", info.Endpoints.First(e => e.Name == "baz").QueueGroup);

        Assert.Equal("foo.bar1", info.Endpoints.First(e => e.Name == "foo.bar1").Subject);
        Assert.Equal("q", info.Endpoints.First(e => e.Name == "foo.bar1").QueueGroup);

        Assert.Equal("grp1.e1", info.Endpoints.First(e => e.Name == "grp1.e1").Subject);
        Assert.Equal("q", info.Endpoints.First(e => e.Name == "grp1.e1").QueueGroup);

        Assert.Equal("grp1.foo.bar2", info.Endpoints.First(e => e.Name == "grp1.e2").Subject);
        Assert.Equal("q", info.Endpoints.First(e => e.Name == "grp1.e2").QueueGroup);

        Assert.Equal("foo.empty1", info.Endpoints.First(e => e.Name == "empty1").Subject);
        Assert.Equal("q_empty", info.Endpoints.First(e => e.Name == "empty1").QueueGroup);
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

    await s2.AddEndpointAsync<int>(
        name: "s2baz",
        subject: "s2foo.baz",
        handler: m => ValueTask.CompletedTask,
        metadata: new Dictionary<string, string> { { "ep-k1", "ep-v1" } },
        cancellationToken: cancellationToken);

    // Check default queue group and stats handler
    {
        var info = (await FindServices<InfoResponse>(nats, "$SRV.INFO.s2", 1, cancellationToken)).First();
        Assert.Single(info.Endpoints);
        var epi = info.Endpoints.First();

        Assert.Equal("s2baz", epi.Name);
        Assert.Equal("s2foo.baz", epi.Subject);
        Assert.Equal("q2", epi.QueueGroup);
        Assert.Equal("ep-v1", epi.Metadata["ep-k1"]);

        var stat = (await FindServices<StatsResponse>(nats, "$SRV.STATS.s2", 1, cancellationToken)).First();
        Assert.Equal("v1", stat.Metadata["k1"]);
        Assert.Equal("v2", stat.Metadata["k2"]);
        Assert.Single(stat.Endpoints);
        var eps = stat.Endpoints.First();
        Assert.Equal("stat-v1", eps.Data["stat-k1"]?.GetValue<string>());
        Assert.Equal("stat-v2", eps.Data["stat-k2"]?.GetValue<string>());
    }
}

async Task ServicesTests2()
{
    Log("Services tests 2...");

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10_0000));
    var cancellationToken = cts.Token;

    // await using var server = NatsServer.Start();
    // await using var nats = server.CreateClientConnection();
    var nats = new NatsConnection();
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

    var info = (await FindServices<InfoResponse>(nats, "$SRV.INFO", 1, cancellationToken)).First();
    Assert.Single(info.Endpoints);
    var endpointInfo = info.Endpoints.First();
    Assert.Equal("e1", endpointInfo.Name);

    for (var i = 0; i < 10; i++)
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

    var stat = (await FindServices<StatsResponse>(nats, "$SRV.STATS", 1, cancellationToken)).First();
    Assert.Single(stat.Endpoints);
    var endpointStats = stat.Endpoints.First();
    Assert.Equal("e1", endpointStats.Name);
    Assert.Equal(10, endpointStats.NumRequests);
    Assert.Equal(3, endpointStats.NumErrors);
    Assert.Equal("Handler error (999)", endpointStats.LastError);
    Assert.True(endpointStats.ProcessingTime > 0);
    Assert.True(endpointStats.AverageProcessingTime > 0);

    Log("OK");
}

static async Task<List<T>> FindServices<T>(NatsConnection nats, string subject, int limit, CancellationToken ct)
{
    var replyOpts = new NatsSubOpts
    {
        Timeout = TimeSpan.FromSeconds(2),
        Serializer = NatsSrvJsonSerializer.Default,
    };
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
