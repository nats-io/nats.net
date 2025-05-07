using System.Text;
using NATS.Client.TestUtilities;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

public class ConsumeResponseTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Consume_response()
    {
        var headers = new Stack<string>();
        headers.Push("NATS/1.0 400 Bad Test Request");
        headers.Push("NATS/1.0 999 Unknown Error");
        headers.Push("NATS/1.0 503");
        headers.Push("NATS/1.0 409 Message Size Exceeds MaxBytes");
        headers.Push("NATS/1.0 409 Leadership Change");

        await using var ms = new MockServer((_, cmd) =>
        {
            if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.INFO"))
            {
                cmd.Reply(payload: """{"stream_name":"x","name":"x"}""");
            }
            else if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.MSG.NEXT"))
            {
                if (headers.Peek() != null)
                {
                    var header = headers.Pop();
                    cmd.Reply(headers: header);
                }
            }

            return Task.CompletedTask;
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        var js = nats.CreateJetStreamContext();
        var consumer = await js.GetConsumerAsync("x", "x", cts.Token);

        var notifications = new List<INatsJSNotification>();
        var exception = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<string>(
                               opts: new NatsJSConsumeOpts
                               {
                                   MaxMsgs = 1000,
                                   IdleHeartbeat = TimeSpan.FromSeconds(1),
                                   NotificationHandler = (n, ct) =>
                                   {
                                       // output.WriteLine($"NOTIFICATION: {n}");
                                       notifications.Add(n);
                                       return Task.CompletedTask;
                                   },
                               },
                               cancellationToken: cts.Token))
            {
            }
        });

        Assert.Equal(400, exception.HeaderCode);
        Assert.Equal("Bad Test Request", exception.HeaderMessageText);

        var types = notifications.Select(n => n.GetType()).ToList();
        Assert.Contains(typeof(NatsJSTimeoutNotification), types);
        Assert.Contains(typeof(NatsJSNoRespondersNotification), types);
        Assert.Contains(typeof(NatsJSLeadershipChangeNotification), types);
        Assert.Contains(typeof(NatsJSMessageSizeExceedsMaxBytesNotification), types);
        Assert.Contains(typeof(NatsJSProtocolNotification), types);
    }
}
