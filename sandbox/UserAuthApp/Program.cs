using Microsoft.Extensions.Logging;
using NATS.Client.Core;

var options = NatsOptions.Default with
{
    Url = "nats://10.10.2.2:4222",
    ConnectOptions = ConnectOptions.Default with { Name = "my client" }
    // LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    // ConnectOptions = ConnectOptions.Default with
    // {
    //     Echo = true,
    //     Username = "foo",
    //     Password = "bar",
    // },
};

await using var conn = new NatsConnection(options, UserCredentials.LoadFromFile(@"C:\Users\User\user_a.creds"));

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
