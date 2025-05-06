using System.Text;
using NATS.Client.TestUtilities;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

public class ConsumeNoResponseTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Consume_no_response()
    {
        await using var ms = new MockServer(
            (client, cmd) =>
            {
                if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.INFO"))
                {
                    // S: MSG <subject> <sid> [reply-to] <#bytes>␍␊[payload]␍␊
                    var json = """{"stream_name":"x","name":"x"}""";
                    client.Writer.Write($"MSG {cmd.ReplyTo} 1 {json.Length}\r\n{json}\r\n");
                    client.Writer.Flush();
                }
                else if (cmd.Name == "PUB" && cmd.Subject.Contains("CONSUMER.MSG.NEXT"))
                {
                    // S: HMSG <subject> <sid> [reply-to] <#header-bytes> <#total-bytes>␍␊[headers]␍␊␍␊[payload]␍␊
                    client.Writer.Write($"HMSG {cmd.ReplyTo} 2 3 0\r\n503\r\n\r\n");
                    client.Writer.Flush();
                }

                return Task.CompletedTask;
            },
            log =>
            {
                output.WriteLine($"LOG: {log}");
            });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        var rtt = await nats.PingAsync();
        output.WriteLine($"rtt={rtt}");
        var js = nats.CreateJetStreamContext();
        var consumer = await js.GetConsumerAsync("x", "x", cts.Token);
        await foreach (var msg in consumer.ConsumeAsync<string>())
        {
            output.WriteLine($"CONSUMED: {msg.Data}");
        }
    }
}
