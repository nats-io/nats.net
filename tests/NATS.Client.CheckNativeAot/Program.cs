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
using Synadia.Orbit.Testing.NatsServerProcessManager;

Log("Starting...");

await RequestReplyTests();
await JetStreamTests();
await KvTests();
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

    await using var server = await NatsServerProcess.StartAsync();
    await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

    var sub = await nats.SubscribeCoreAsync<int>("foo");
    var reg = sub.Register(async msg =>
    {
        await msg.ReplyAsync(msg.Data * 2);
        await msg.ReplyAsync(msg.Data * 3);
        await msg.ReplyAsync(msg.Data * 4);
        await msg.ReplyAsync<int?>(null); // sentinel
    });

    // make sure subs have started
    await nats.PingAsync();

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var results = new[] { 2, 3, 4 };
    var count = 0;
    await foreach (var msg in nats.RequestManyAsync<int, int?>("foo", 1, cancellationToken: cts.Token))
    {
        AssertEqual(results[count++], msg.Data);
    }

    AssertEqual(3, count);

    await sub.DisposeAsync();
    await reg;

    Log("OK");
}

async Task JetStreamTests()
{
    Log("JetStream tests...");

    await using var server = await NatsServerProcess.StartAsync();
    await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

    // Happy user
    {
        var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var js = new NatsJSContext(nats);

        // Create stream
        var stream = await js.CreateStreamAsync(
            config: new StreamConfig("events", ["events.*"]),
            cancellationToken: cts1.Token);
        AssertEqual("events", stream.Info.Config.Name);

        // Create consumer
        var consumer = await js.CreateOrUpdateConsumerAsync(
            "events",
            new ConsumerConfig
            {
                Name = "consumer1",
                DurableName = "consumer1",
            },
            cts1.Token);
        AssertEqual("events", consumer.Info.StreamName);
        AssertEqual("consumer1", consumer.Info.Config.Name);

        // Publish
        var ack = await js.PublishAsync("events.foo", new TestData { Test = 1 }, serializer: TestDataJsonSerializer<TestData>.Default, cancellationToken: cts1.Token);
        AssertNull(ack.Error);
        AssertEqual("events", ack.Stream);
        AssertEqual(1, (int)ack.Seq);
        AssertFalse(ack.Duplicate);

        // Message ID
        ack = await js.PublishAsync(
            "events.foo",
            new TestData { Test = 2 },
            serializer: TestDataJsonSerializer<TestData>.Default,
            opts: new NatsJSPubOpts { MsgId = "test2" },
            cancellationToken: cts1.Token);
        AssertNull(ack.Error);
        AssertEqual("events", ack.Stream);
        AssertEqual(2, (int)ack.Seq);
        AssertFalse(ack.Duplicate);

        // Duplicate
        ack = await js.PublishAsync(
            "events.foo",
            new TestData { Test = 2 },
            serializer: TestDataJsonSerializer<TestData>.Default,
            opts: new NatsJSPubOpts { MsgId = "test2" },
            cancellationToken: cts1.Token);
        AssertNull(ack.Error);
        AssertEqual("events", ack.Stream);
        AssertEqual(2, (int)ack.Seq);
        AssertTrue(ack.Duplicate);

        // Consume
        var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var messages = new List<INatsJSMsg<TestData>>();
        await foreach (var msg in consumer.ConsumeAsync(serializer: TestDataJsonSerializer<TestData>.Default, new NatsJSConsumeOpts { MaxMsgs = 100 }, cancellationToken: cts2.Token))
        {
            messages.Add(msg);

            // Only ACK one message so we can consume again
            if (messages.Count == 1)
            {
                await msg.AckAsync(cancellationToken: cts2.Token);
            }

            if (messages.Count == 2)
            {
                break;
            }
        }

        AssertEqual(2, messages.Count);
        AssertEqual("events.foo", messages[0].Subject);
        AssertEqual("events.foo", messages[1].Subject);
    }

    // Handle errors
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var js = new NatsJSContext(nats);
        var exception = await AssertThrowsAsync<NatsJSApiException>(async () =>
        {
            await js.CreateStreamAsync(
                config: new StreamConfig("events2", ["events.*"]),
                cancellationToken: cts.Token);
        });
        AssertEqual(400, exception.Error.Code);

        // subjects overlap with an existing stream
        AssertEqual(10065, exception.Error.ErrCode);
    }

    // Delete stream
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var js = new NatsJSContext(nats);

        // Success
        await js.DeleteStreamAsync("events", cts.Token);

        // Error
        var exception = await AssertThrowsAsync<NatsJSApiException>(async () =>
        {
            await js.DeleteStreamAsync("events2", cts.Token);
        });

        AssertEqual(404, exception.Error.Code);

        // stream not found
        AssertEqual(10059, exception.Error.ErrCode);
    }

    Log("OK");
}

async Task KvTests()
{
    Log("KV tests...");

    await using var server = await NatsServerProcess.StartAsync();
    await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

    var js = new NatsJSContext(nats);
    var kv = new NatsKVContext(js);

    var store = await kv.CreateStoreAsync("b1");

    await store.PutAsync("k1", "v1");

    var entry = await store.GetEntryAsync<string>("k1");
    AssertEqual("v1", entry.Value);

    Log("OK");
}

async Task ObjectStoreTests()
{
    Log("Object store tests...");

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var cancellationToken = cts.Token;

    await using var server = await NatsServerProcess.StartAsync();
    await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
    var js = new NatsJSContext(nats);
    var ob = new NatsObjContext(js);

    var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

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
        AssertEqual(buffer90, Encoding.ASCII.GetString(buffer));
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
        AssertEqual(buffer90 + "09-45", Encoding.ASCII.GetString(buffer));
    }

    Log("OK");
}

async Task ServicesTests()
{
    Log("Services tests...");

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var cancellationToken = cts.Token;

    await using var server = await NatsServerProcess.StartAsync();
    await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
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

    // make sure subs have started
    await nats.PingAsync();

    // Check that the endpoints are registered correctly
    {
        var info = (await nats.FindServicesAsync("$SRV.INFO.s1", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken)).First();
        AssertEqual(5, info.Endpoints.Count);

        AssertEqual("foo.baz", info.Endpoints.First(e => e.Name == "baz").Subject);
        AssertEqual("q", info.Endpoints.First(e => e.Name == "baz").QueueGroup);

        AssertEqual("foo.bar1", info.Endpoints.First(e => e.Name == "foo-bar1").Subject);
        AssertEqual("q", info.Endpoints.First(e => e.Name == "foo-bar1").QueueGroup);

        AssertEqual("grp1.e1", info.Endpoints.First(e => e.Name == "e1").Subject);
        AssertEqual("q", info.Endpoints.First(e => e.Name == "e1").QueueGroup);

        AssertEqual("grp1.foo.bar2", info.Endpoints.First(e => e.Name == "e2").Subject);
        AssertEqual("q", info.Endpoints.First(e => e.Name == "e2").QueueGroup);

        AssertEqual("foo.empty1", info.Endpoints.First(e => e.Name == "empty1").Subject);
        AssertEqual("q_empty", info.Endpoints.First(e => e.Name == "empty1").QueueGroup);
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

    // make sure subs have started
    await nats.PingAsync();

    // Check default queue group and stats handler
    {
        var info = (await nats.FindServicesAsync("$SRV.INFO.s2", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken)).First();
        AssertSingle(info.Endpoints);
        var epi = info.Endpoints.First();

        AssertEqual("s2baz", epi.Name);
        AssertEqual("s2foo.baz", epi.Subject);
        AssertEqual("q2", epi.QueueGroup);
        AssertEqual("ep-v1", epi.Metadata["ep-k1"]);

        var stat = (await nats.FindServicesAsync("$SRV.STATS.s2", 1, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken)).First();
        AssertEqual("v1", stat.Metadata["k1"]);
        AssertEqual("v2", stat.Metadata["k2"]);
        AssertSingle(stat.Endpoints);
        var eps = stat.Endpoints.First();
        AssertEqual("stat-v1", eps.Data["stat-k1"]?.GetValue<string>());
        AssertEqual("stat-v2", eps.Data["stat-k2"]?.GetValue<string>());
        AssertEqual("s2baz", eps.Data["ep_name"]?.GetValue<string>());
    }

    Log("OK");
}

async Task ServicesTests2()
{
    Log("Services tests 2...");

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10_0000));
    var cancellationToken = cts.Token;

    await using var server = await NatsServerProcess.StartAsync();
    await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
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

    // make sure subs have started
    await nats.PingAsync();

    var info = (await nats.FindServicesAsync("$SRV.INFO", 1, NatsSrvJsonSerializer<InfoResponse>.Default, cancellationToken)).First();
    AssertSingle(info.Endpoints);
    var endpointInfo = info.Endpoints.First();
    AssertEqual("e1", endpointInfo.Name);

    for (var i = 0; i < 10; i++)
    {
        var response = await nats.RequestAsync<int, int>(endpointInfo.Subject, i, cancellationToken: cancellationToken);
        if (i is 7 or 8)
        {
            AssertEqual($"{i}", response.Headers?["Nats-Service-Error-Code"][0]);
            AssertEqual($"Error{i}", response.Headers?["Nats-Service-Error"][0]);
        }
        else if (i is 9)
        {
            AssertEqual("999", response.Headers?["Nats-Service-Error-Code"][0]);
            AssertEqual("Handler error", response.Headers?["Nats-Service-Error"][0]);
        }
        else
        {
            AssertEqual(i * i, response.Data);
            AssertNull(response.Headers);
        }
    }

    var stat = (await nats.FindServicesAsync("$SRV.STATS", 1, NatsSrvJsonSerializer<StatsResponse>.Default, cancellationToken)).First();
    AssertSingle(stat.Endpoints);
    var endpointStats = stat.Endpoints.First();
    AssertEqual("e1", endpointStats.Name);
    AssertEqual(10L, endpointStats.NumRequests);
    AssertEqual(3L, endpointStats.NumErrors);
    AssertEqual("999:Handler error", endpointStats.LastError);
    AssertTrue(endpointStats.ProcessingTime > 0);
    AssertTrue(endpointStats.AverageProcessingTime > 0);

    Log("OK");
}

void AssertEqual(object? q, object? b)
{
    if (!Equals(q, b))
        throw new Exception($"Expected {q} but got {b}");
}

void AssertTrue(bool a)
{
    if (!a)
        throw new Exception("Expected true");
}

void AssertFalse(bool a)
{
    if (a)
        throw new Exception("Expected false");
}

void AssertNull(object? a)
{
    if (a != null)
        throw new Exception("Expected null");
}

void AssertSingle<T>(ICollection<T> a)
{
    if (a.Count != 1)
        throw new Exception("Expected single");
}

async Task<T> AssertThrowsAsync<T>(Func<Task> func)
    where T : Exception
{
    try
    {
        await func();
        throw new Exception("Expected exception");
    }
    catch (T e)
    {
        return e;
    }
}
