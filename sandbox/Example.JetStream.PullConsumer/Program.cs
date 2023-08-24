using System.Threading.Channels;
using Example.JetStream.PullConsumer;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var consumer = await js.CreateConsumerAsync("s1", "c1");


var idle = TimeSpan.FromSeconds(1);
var expires = TimeSpan.FromSeconds(10);
var batch = 10;

await using var sub = await consumer.CreateSubscription<RawData>(
    batch,
    expires,
    idle,
    opts: new NatsSubOpts
    {
        Serializer = new RawDataSerializer(),
        ChannelOptions = new NatsSubChannelOpts
        {
            Capacity = 1,
            FullMode = BoundedChannelFullMode.Wait,
        },
    });


await sub.CallMsgNextAsync(new ConsumerGetnextRequest
{
    Batch = batch,
    IdleHeartbeat = idle.ToNanos(),
    Expires = expires.ToNanos(),
});
sub.ResetPending();
int count = 0;

await foreach (var jsMsg in sub.Msgs.ReadAllAsync())
{
    var msg = jsMsg.Msg;
    // Console.WriteLine($"____");
    // Console.WriteLine($"subject: {msg.Subject}");
    // if (count++ % 100 == 0)
    Console.WriteLine($"data: {msg.Data}");
    await jsMsg.AckAsync();
}


