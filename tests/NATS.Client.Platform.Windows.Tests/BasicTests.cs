using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NATS.Client.ObjectStore;
using NATS.Client.Serializers.Json;
using NATS.Client.Services;
using Xunit.Abstractions;

namespace NATS.Client.Platform.Windows.Tests;

public class BasicTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Core()
    {
        await using var server = await NatsServerProcess.StartAsync(output.WriteLine);
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, SerializerRegistry = NatsJsonSerializerRegistry.Default });

        await nats.PingAsync();

        await using var sub = await nats.SubscribeCoreAsync<TestData>("foo");
        for (var i = 0; i < 16; i++)
        {
            await nats.PublishAsync("foo", new TestData { Id = i });
            Assert.Equal(i, (await sub.Msgs.ReadAsync()).Data!.Id);
        }
    }

    [Fact]
    public async Task JetStream()
    {
        await using var server = await NatsServerProcess.StartAsync(output.WriteLine);
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", ["s1.>"]));

        for (var i = 0; i < 16; i++)
        {
            var ack = await js.PublishAsync("s1.foo", $"bar{i}");
            ack.EnsureSuccess();
        }

        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("c1"));

        var count = 0;
        await foreach (var msg in consumer.ConsumeAsync<string>())
        {
            await msg.AckAsync();
            Assert.Equal($"bar{count++}", msg.Data);
            if (count == 16)
            {
                break;
            }
        }

        var orderedConsumer = await js.CreateOrderedConsumerAsync("s1");
        count = 0;
        await foreach (var msg in orderedConsumer.ConsumeAsync<string>())
        {
            Assert.Equal($"bar{count++}", msg.Data);
            if (count == 16)
            {
                break;
            }
        }
    }

    [Fact]
    public async Task KV()
    {
        await using var server = await NatsServerProcess.StartAsync(output.WriteLine);
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync("b1");

        for (var i = 0; i < 16; i++)
        {
            await store.PutAsync($"k{i}", $"v{i}");
        }

        for (var i = 0; i < 16; i++)
        {
            var entry = await store.GetEntryAsync<string>($"k{i}");
            Assert.Equal($"v{i}", entry.Value);
        }
    }

    [Fact]
    public async Task ObjectStore()
    {
        await using var server = await NatsServerProcess.StartAsync(output.WriteLine);
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var store = await obj.CreateObjectStoreAsync("b1");

        for (var i = 0; i < 16; i++)
        {
            await store.PutAsync($"k{i}", [(byte)i]);
        }

        for (var i = 0; i < 16; i++)
        {
            var bytes = await store.GetBytesAsync($"k{i}");
            Assert.Equal([(byte)i], bytes);
        }
    }

    [Fact]
    public async Task Services()
    {
        await using var server = await NatsServerProcess.StartAsync(output.WriteLine);
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var svc = new NatsSvcContext(nats);

        var s1 = await svc.AddServiceAsync("s1", "1.0.0");

        await s1.AddEndpointAsync<int>(
            async msg =>
            {
                await msg.ReplyAsync(msg.Data * 2);
            },
            "multiply");

        for (var i = 0; i < 16; i++)
        {
            var reply = await nats.RequestAsync<int, int>("multiply", i);
            Assert.Equal(i * 2, reply.Data);
        }
    }

    private class TestData
    {
        public int Id { get; set; }
    }
}
