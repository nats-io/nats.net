using NATS.Client.Core;
#pragma warning disable CS4014

var natsKey1 = new NatsKey("subject1");
var natsKey2 = new NatsKey("subject2");

await using var subConnection1 = new NatsConnection(NatsOptions.Default with
{
    Url = "localhost:4222"
});
await subConnection1.ConnectAsync();

using var d1 = await subConnection1.SubscribeAsync<string>(natsKey1, x =>
{
    Console.WriteLine($"\tSUB1:{x}");
});

using var d1n = await subConnection1.SubscribeAsync<int>(natsKey2, x =>
{
    Console.WriteLine($"\tSUB1:{x}");
});

await using var subConnection2 = new NatsConnection(NatsOptions.Default with
{
    Url = "localhost:4222"
});

await subConnection2.ConnectAsync();

using var d2 = await subConnection2.SubscribeAsync<string>(natsKey1, x =>
{
    Console.WriteLine($"\tSUB2:{x}");
});

using var d2n = await subConnection2.SubscribeAsync<int>(natsKey2, x =>
{
    Console.WriteLine($"\tSUB2:{x}");
});

var cts = new CancellationTokenSource();

Task.Run(async () =>
{
    await using var pubConnection1 = new NatsConnection(NatsOptions.Default with
    {
        Url = "localhost:4222"
    });

    await pubConnection1.ConnectAsync();

    await using var pubConnection2 = new NatsConnection(NatsOptions.Default with
    {
        Url = "localhost:4222"
    });

    await pubConnection2.ConnectAsync();

    var random = new Random();
    while (!cts.IsCancellationRequested)
    {
        var time = DateTime.Now.ToString();
        pubConnection1.PublishAsync(natsKey1, time);
        Console.WriteLine($"PUB1:{time}");

        var number = random.Next(0, 1000);
        pubConnection2.PublishAsync(natsKey2, number);
        Console.WriteLine($"PUB2:{number}");

        await Task.Delay(1000, cts.Token);
    }
}, cts.Token);

Console.ReadKey();
cts.Cancel();

Console.WriteLine("end");
