// See https://aka.ms/new-console-template for more information

using System.Text;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using NATS.Client.ObjectStore;
using NATS.Client.Services;
using NATS.Net;

CancellationTokenSource cts = new();

await using var client = new NatsClient();

// Subscribe for int, string, bytes, json
List<Task> tasks =
[
    Task.Run(async () =>
    {
        await foreach (var msg in client.SubscribeAsync<int>("x.int", cancellationToken: cts.Token))
        {
            Console.WriteLine($"Received int: {msg.Data}");
        }
    }),

    Task.Run(async () =>
    {
        await foreach (var msg in client.SubscribeAsync<string>("x.string", cancellationToken: cts.Token))
        {
            Console.WriteLine($"Received string: {msg.Data}");
        }
    }),

    Task.Run(async () =>
    {
        await foreach (var msg in client.SubscribeAsync<byte[]>("x.bytes", cancellationToken: cts.Token))
        {
            if (msg.Data != null)
            {
                Console.WriteLine($"Received bytes: {Encoding.UTF8.GetString(msg.Data)}");
            }
        }
    }),

    Task.Run(async () =>
    {
        await foreach (var msg in client.SubscribeAsync<MyData>("x.json", cancellationToken: cts.Token))
        {
            Console.WriteLine($"Received data: {msg.Data}");
        }
    }),

    Task.Run(async () =>
    {
        await foreach (var msg in client.SubscribeAsync<MyData>("x.service", cancellationToken: cts.Token))
        {
            if (msg.Data != null)
            {
                Console.WriteLine($"Replying to data: {msg.Data}");
                await msg.ReplyAsync($"Thank you {msg.Data.Name} your Id is {msg.Data.Id}!");
            }
        }
    }),

    Task.Run(async () =>
    {
        var id = 0;
        await foreach (var msg in client.SubscribeAsync<object>("x.service2", cancellationToken: cts.Token))
        {
            await msg.ReplyAsync(new MyData(id++, $"foo{id}"));
        }
    })
];

await Task.Delay(1000);

await client.PublishAsync("x.int", 100);
await client.PublishAsync("x.string", "Hello, World!");
await client.PublishAsync("x.bytes", new byte[] { 65, 66, 67 });
await client.PublishAsync("x.json", new MyData(30, "bar"));

// Request/Reply
{
    var response = await client.RequestAsync<MyData, string>("x.service", new MyData(100, "foo"));
    Console.WriteLine($"Response: {response.Data}");
}

// Request/Reply without request data
for (var i = 0; i < 3; i++)
{
    var response = await client.RequestAsync<MyData>("x.service2");
    Console.WriteLine($"Response[{i}]: {response.Data}");
}

// Use JetStream by referencing NATS.Client.JetStream package
var js = client.CreateJetStreamContext();
await foreach (var stream in js.ListStreamsAsync())
{
    Console.WriteLine($"JetStream Stream: {stream.Info.Config.Name}");
}

// Use KeyValueStore by referencing NATS.Client.KeyValueStore package
var kv1 = client.CreateKeyValueStoreContext();
var kv2 = js.CreateKeyValueStoreContext();
await kv1.CreateStoreAsync("store1");
await kv2.CreateStoreAsync("store1");

// Use ObjectStore by referencing NATS.Client.ObjectStore package
var obj1 = client.CreateObjectStoreContext();
var obj2 = js.CreateObjectStoreContext();
await obj1.CreateObjectStoreAsync("store1");
await obj2.CreateObjectStoreAsync("store1");

// Use Services by referencing NATS.Client.Services package
var svc = client.CreateServicesContext();
await svc.AddServiceAsync("service1", "1.0.0");

await cts.CancelAsync();

await Task.WhenAll(tasks);

Console.WriteLine("Bye!");

public record MyData(int Id, string Name);
