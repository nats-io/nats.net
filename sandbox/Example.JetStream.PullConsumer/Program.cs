using Example.JetStream.PullConsumer;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var consumer = await js.CreateConsumerAsync("s1", "c1");

var idle = TimeSpan.FromSeconds(1);
var expires = TimeSpan.FromSeconds(10);
var batch = 10;

if (args.Length > 0 && args[0] == "fetch")
{
    while (true)
    {
        Console.WriteLine($"___\nFETCH {batch}");
        await using var sub = await consumer.FetchAsync<RawData>(new NatsJSFetchOpts
        {
            MaxMsgs = batch, Expires = expires, IdleHeartbeat = idle, Serializer = new RawDataSerializer(),
        });
        await foreach (var jsMsg in sub.Msgs.ReadAllAsync())
        {
            var msg = jsMsg.Msg;
            Console.WriteLine($"data: {msg.Data}");
            await jsMsg.AckAsync();
        }
    }
}
else if (args.Length > 0 && args[0] == "fetch-all")
{
    while (true)
    {
        Console.WriteLine($"___\nFETCH {batch}");
        var opts = new NatsJSFetchOpts
        {
            MaxMsgs = batch, Expires = expires, IdleHeartbeat = idle, Serializer = new RawDataSerializer(),
        };
        await foreach (var jsMsg in consumer.FetchAllAsync<RawData>(opts))
        {
            var msg = jsMsg.Msg;
            Console.WriteLine($"data: {msg.Data}");
            await jsMsg.AckAsync();
        }
    }
}
else if (args.Length > 0 && args[0] == "next")
{
    while (true)
    {
        Console.WriteLine("___\nNEXT");
        var next = await consumer.NextAsync<RawData>(new NatsJSNextOpts
        {
            Expires = expires, IdleHeartbeat = idle, Serializer = new RawDataSerializer(),
        });
        if (next is { } jsMsg)
        {
            var msg = jsMsg.Msg;
            Console.WriteLine($"data: {msg.Data}");
            await jsMsg.AckAsync();
        }
    }
}
else if (args.Length > 0 && args[0] == "consume")
{
    Console.WriteLine("___\nCONSUME");
    await using var sub = await consumer.ConsumeAsync<RawData>(new NatsJSConsumeOpts
    {
        MaxMsgs = batch, Expires = expires, IdleHeartbeat = idle, Serializer = new RawDataSerializer(),
    });

    await foreach (var jsMsg in sub.Msgs.ReadAllAsync())
    {
        var msg = jsMsg.Msg;
        Console.WriteLine($"data: {msg.Data}");
        await jsMsg.AckAsync();
    }
}
else if (args.Length > 0 && args[0] == "consume-all")
{
    Console.WriteLine("___\nCONSUME-ALL");
    var opts = new NatsJSConsumeOpts
    {
        MaxMsgs = batch, Expires = expires, IdleHeartbeat = idle, Serializer = new RawDataSerializer(),
    };

    await foreach (var jsMsg in consumer.ConsumeAllAsync<RawData>(opts))
    {
        var msg = jsMsg.Msg;
        Console.WriteLine($"data: {msg.Data}");
        await jsMsg.AckAsync();
    }
}
