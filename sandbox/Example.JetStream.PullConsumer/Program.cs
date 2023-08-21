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

await using var sub = await consumer.CreateSubscription<RawData, State>(
    state: new State(),
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

public class State : INatsJSSubState
{
    public ValueTask ReceivedControlMsgAsync(INatsJSSub sub, NatsJSControlMsg controlMsg)
    {
        Console.WriteLine($"CTRL: {controlMsg.Name}");
        return ValueTask.CompletedTask;
    }

    public void ResetHeartbeatTimer(INatsJSSub sub)
    {
        Console.WriteLine($"RESET HB");
    }

    public void ReceivedUserMsg(INatsJSSub sub)
    {
        Console.WriteLine($"RCV MSG");
    }

    public ConsumerGetnextRequest? ReconnectRequestFactory(INatsJSSub sub)
    {
        Console.WriteLine("RECONNECT");
        return null;
    }

    public ValueTask ReadyAsync(INatsJSSub sub)
    {
        return sub.CallMsgNextAsync(new ConsumerGetnextRequest
        {
            Batch = 10,
            Expires = TimeSpan.FromSeconds(30).ToNanos(),
            IdleHeartbeat = TimeSpan.FromSeconds(15).ToNanos(),
        });
    }
}
