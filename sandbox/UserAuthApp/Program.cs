using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var options = NatsOptions.Default with
{
    Url = "nats://10.10.2.2:4222",
    Name = "my client",
    AuthOptions = NatsAuthOptions.Default with
    {
        CredsFile = @"C:\Users\User\user_a.creds"
    }
};

await using var conn = new NatsConnection(options);

// subscribe
// var subscription = await conn.SubscribeAsync<Person>("foo", x =>
// {
//     Console.WriteLine($"Received {x}");
// });

// await conn.PublishAsync("foo", new Person(30, "bar"));
//
// public record Person(int Age, string Name);

var subscription = await conn.SubscribeAsync<int>("foo", x =>
{
    Console.WriteLine($"Received {x}");
});
await conn.PublishAsync("foo", (int)DateTime.Now.TimeOfDay.TotalSeconds);

Console.ReadLine();

subscription.Dispose();
