// See https://aka.ms/new-console-template for more information

using System.Text;
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
// var js = client.GetJetStream();
await cts.CancelAsync();

await Task.WhenAll(tasks);

Console.WriteLine("Bye!");

public record MyData(int Id, string Name);
