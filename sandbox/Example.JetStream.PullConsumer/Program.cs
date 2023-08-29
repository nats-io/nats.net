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

await using var sub = await consumer.ConsumeAsync<RawData>(new NatsJSConsumeOpts
{
    MaxMsgs = batch,
    Expires = expires,
    IdleHeartbeat = idle,
    Serializer = new RawDataSerializer(),
});

await foreach (var jsMsg in sub.Msgs.ReadAllAsync())
{
    var msg = jsMsg.Msg;
    Console.WriteLine($"data: {msg.Data}");
    await jsMsg.AckAsync();
}
