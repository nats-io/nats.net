using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.ObjectStore;
using NATS.Client.ObjectStore.Models;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Nats.Client.Compat;

public class ObjectStoreCompat
{
    public async Task TestDefaultBucket(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));

        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var config = json["config"];

        await ob.CreateObjectStore(new NatsObjConfig(config["bucket"].GetValue<string>()));
        await msg.ReplyAsync();
    }

    public async Task TestCustomBucket(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));

        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var config = json["config"];

        Enum.TryParse(config["storage"].GetValue<string>(), true, out NatsObjStorageType storage);
        var description = config["description"].GetValue<string>();
        var fromSeconds = TimeSpan.FromSeconds(config["max_age"].GetValue<long>() / 1_000_000_000.0);
        var bucket = config["bucket"].GetValue<string>();
        var numberOfReplicas = config["num_replicas"].GetValue<int>();

        await ob.CreateObjectStore(new NatsObjConfig(bucket)
        {
            Description = description,
            MaxAge = fromSeconds,
            Storage = storage,
            NumberOfReplicas = numberOfReplicas,
        });

        await msg.ReplyAsync();
    }

    public async Task TestPutObject(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var url = json["url"].GetValue<string>();
        var config = json["config"];

        var store = await ob.GetObjectStoreAsync("test");

        await store.PutAsync(
            new ObjectMetadata
            {
                Name = config["name"].GetValue<string>(),
                Description = config["description"].GetValue<string>(),
            },
            await new HttpClient().GetStreamAsync(url));

        await msg.ReplyAsync();
    }

    public async Task TestGetObject(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var bucket = json["bucket"].GetValue<string>();
        var objectName = json["object"].GetValue<string>();

        var store = await ob.GetObjectStoreAsync(bucket);

        var bytes = await store.GetBytesAsync(objectName);

        var sha256 = SHA256.HashData(bytes);

        await msg.ReplyAsync(sha256);
    }

    public async Task TestUpdateMetadata(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var bucket = json["bucket"].GetValue<string>();
        var objectName = json["object"].GetValue<string>();
        var name = json["config"]["name"].GetValue<string>();
        var description = json["config"]["description"].GetValue<string>();

        var store = await ob.GetObjectStoreAsync(bucket);

        await store.UpdateMetaAsync(objectName, new ObjectMetadata
        {
            Name = name,
            Description = description,
        });

        await msg.ReplyAsync();
    }

    public async Task TestWatchUpdates(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var bucket = json["bucket"].GetValue<string>();

        var store = await ob.GetObjectStoreAsync(bucket);

        await foreach (var info in store.WatchAsync(new NatsObjWatchOpts { UpdatesOnly = true }))
        {
            await msg.ReplyAsync(info.Digest);
            break;
        }
    }

    public async Task TestWatch(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var bucket = json["bucket"].GetValue<string>();

        var store = await ob.GetObjectStoreAsync(bucket);

        var list = new List<string>();
        await foreach (var info in store.WatchAsync())
        {
            list.Add(info.Digest);
            if (list.Count == 2)
                break;
        }

        await msg.ReplyAsync($"{list[0]},{list[1]}");
    }

    public async Task TestGetLink(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var bucket = json["bucket"].GetValue<string>();
        var objectName = json["object"].GetValue<string>();

        var store = await ob.GetObjectStoreAsync(bucket);

        var bytes = await store.GetBytesAsync(objectName);

        var sha256 = SHA256.HashData(bytes);

        await msg.ReplyAsync(sha256);
    }

    public async Task TestPutLink(NatsConnection nats, NatsMsg<Memory<byte>> msg)
    {
        var ob = new NatsObjContext(new NatsJSContext(nats));
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var bucket = json["bucket"].GetValue<string>();
        var objectName = json["object"].GetValue<string>();
        var linkName = json["link_name"].GetValue<string>();

        var store = await ob.GetObjectStoreAsync(bucket);

        var target = await store.GetInfoAsync(objectName);

        await store.AddLinkAsync(linkName, target);

        await msg.ReplyAsync();
    }
}
