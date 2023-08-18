using Example.JetStream.PullConsumer;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var consumer = await js.CreateConsumerAsync("s1", "c1");

static async Task ControlHandler(NatsJSSub<RawData, object> s, NatsJSControlMsg msg)
{
    Console.WriteLine($"________________________________________________");
    Console.WriteLine($"| {msg.Name} | {msg.Headers?.Code} {msg.Headers?.MessageText}");
    var error = msg.Error?.Error;
    if (error != null) Console.WriteLine($"| Error: {error}");
    Console.WriteLine($"------------------------------------------------");

    if (msg == NatsJSControlMsg.HeartBeat)
    {
        Console.WriteLine(">>HeartBeat");
        s.ResetHeartbeatTimer(TimeSpan.FromSeconds(5));
        await s.CallMsgNextAsync(new ConsumerGetnextRequest
        {
            Batch = 10,
            Expires = TimeSpan.FromSeconds(10).ToNanos(),
            IdleHeartbeat = TimeSpan.FromSeconds(1).ToNanos(),
        });
    }
    else if (msg == NatsJSControlMsg.BatchComplete)
    {
        Console.WriteLine(">>BatchComplete");
        s.ResetHeartbeatTimer(TimeSpan.FromSeconds(5));
        await s.CallMsgNextAsync(new ConsumerGetnextRequest
        {
            Batch = 10,
            Expires = TimeSpan.FromSeconds(10).ToNanos(),
            IdleHeartbeat = TimeSpan.FromSeconds(1).ToNanos(),
        });
    }
    else if (msg == NatsJSControlMsg.BatchHighWatermark)
    {
        Console.WriteLine(">>BatchHighWatermark");
        s.ResetHeartbeatTimer(TimeSpan.FromSeconds(5));
        await s.CallMsgNextAsync(new ConsumerGetnextRequest
        {
            Batch = 10,
            Expires = TimeSpan.FromSeconds(10).ToNanos(),
            IdleHeartbeat = TimeSpan.FromSeconds(1).ToNanos(),
        });
    }
    else if (msg.Headers != null)
    {
        if (msg.Headers.Code == 408 && msg.Headers.Message == NatsHeaders.Messages.RequestTimeout)
        {
            // await s.CallMsgNextAsync(new ConsumerGetnextRequest
            // {
            //     Batch = 10,
            //     Expires = TimeSpan.FromSeconds(10).ToNanos(),
            //     IdleHeartbeat = TimeSpan.FromSeconds(1).ToNanos(),
            // });
        }
    }
}

static ConsumerGetnextRequest? ReconnectRequestFactory(NatsJSSub<RawData, object> s) => default;

await using var sub = await consumer.CreateSubscription<RawData, object>(
    controlHandler: ControlHandler,
    reconnectRequestFactory: ReconnectRequestFactory,
    state: new object(),
    heartBeat: TimeSpan.FromSeconds(5),
    consumerOpts: new NatsJSSubOpts(10, 5),
    opts: new NatsSubOpts { Serializer = new RawDataSerializer() });

await sub.CallMsgNextAsync(new ConsumerGetnextRequest
{
    Batch = 10,
    IdleHeartbeat = TimeSpan.FromSeconds(1).ToNanos(),
    Expires = TimeSpan.FromSeconds(10).ToNanos(),
});

await foreach (var jsMsg in sub.Msgs.ReadAllAsync())
{
    var msg = jsMsg.Msg;
    await jsMsg.AckAsync();
    Console.WriteLine($"____");
    Console.WriteLine($"subject: {msg.Subject}");
    Console.WriteLine($"data: {msg.Data}");
}
