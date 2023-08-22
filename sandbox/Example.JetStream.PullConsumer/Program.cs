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
var batch = 1000;

await using var sub = await consumer.CreateSubscription<RawData, State>(
    state: new State(batch, expires, idle),
    opts: new NatsSubOpts { Serializer = new RawDataSerializer() });


await sub.CallMsgNextAsync(new ConsumerGetnextRequest
{
    Batch = batch,
    IdleHeartbeat = idle.ToNanos(),
    Expires = expires.ToNanos(),
});

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

public class State : INatsJSSubState
{
    private readonly int _batch;
    private readonly long _expires;
    private readonly long _idle;
    private readonly Timer _timer;
    private readonly TimeSpan _timeout;
    private int _pending;
    private volatile INatsJSSub? _sub;

    public State(int batch, TimeSpan expires, TimeSpan idle)
    {
        _batch = batch;
        _expires = expires.ToNanos();
        _idle = idle.ToNanos();
        _timeout = idle * 2;
        _pending = _batch;

        _timer = new Timer(
            state =>
            {
                var self = (State)state!;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} HB tick");
                self.Pull();
            },
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public void SetSub(INatsJSSub sub) => _sub = sub;

    public ValueTask ReceivedControlMsgAsync(NatsJSControlMsg controlMsg)
    {
        // Console.WriteLine($"CTRL: {controlMsg.Type}");
        if (controlMsg is { Type: NatsJSControlType.Headers, Headers: { } headers })
        {
            // Nats-Pending-Messages: 7
            // Nats-Pending-Bytes
            if (headers.TryGetValue("Nats-Pending-Messages", out var natsPendingMsgs))
            {
                Console.WriteLine($"{headers.Dump()}");

                if (int.TryParse(natsPendingMsgs, out var pendingMsgs))
                {
                    var pending = Interlocked.Add(ref _pending, -pendingMsgs);
                    Console.WriteLine($"pending:{pending} pendingMsgs:{pendingMsgs}");
                }
            }

            if (headers is { Code: 408, Message: NatsHeaders.Messages.RequestTimeout })
            {
                Pull();
            }
            else if (headers is { Code: 100, Message: NatsHeaders.Messages.IdleHeartbeat })
            {
            }
            else
            {
                Console.WriteLine($"{headers.Dump()}");
            }
        }

        return ValueTask.CompletedTask;
    }

    public void ResetHeartbeatTimer()
    {
        // Console.WriteLine($"RESET HB");
        _timer.Change(_timeout, _timeout);
    }

    public void ReceivedUserMsg()
    {
        var pending = Interlocked.Decrement(ref _pending);
        if (pending <= _batch / 2)
        {
            Pull();
        }

        // Console.WriteLine($"RCV MSG");
    }

    private void Pull()
    {
        var batch = _batch - Volatile.Read(ref _pending);
        if (batch == 0)
            batch = _batch;
        _sub?.CallMsgNextAsync(new ConsumerGetnextRequest
        {
            Batch = batch,
            IdleHeartbeat = _idle,
            Expires = _expires,
        });
        var pending = Interlocked.Add(ref _pending, batch);
        Console.WriteLine($"PULL>>> batch:{batch} pending:{pending}");

    }

    public ConsumerGetnextRequest? ReconnectRequestFactory()
    {
        Console.WriteLine("RECONNECT");
        var batch = _batch - Volatile.Read(ref _pending);
        if (batch <= 0)
            batch = _batch;
        var pending = Interlocked.Add(ref _pending, batch);
        Console.WriteLine($"PULL>>> batch:{batch} pending:{pending}");
        return new ConsumerGetnextRequest
        {
            Batch = batch,
            IdleHeartbeat = _idle,
            Expires = _expires,
        };
    }
}
