using System.Diagnostics;
using NATS.Client.Core;

// await using
    var conn = new NatsConnection();

int msgCount = 100_000;
CountdownEvent latch = new CountdownEvent(msgCount);

var subscription = await conn.SubscribeAsync<NotRecord>("foo", x =>
{
    try
    {
        Console.WriteLine($"Received {x}");
        latch.Signal();
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
});

Stopwatch sw = Stopwatch.StartNew();

// publish
for (int x = 0; x < msgCount; x++)
{
#pragma warning disable CS4014
    conn.PublishAsync("foo", new Record(x));
#pragma warning restore CS4014
}
sw.Stop();
latch.Wait();

    Console.WriteLine("TIME " + sw.ElapsedMilliseconds);

public record Record(int id);
public record NotRecord(String id);
