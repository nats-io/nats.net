using System.Buffers;
using System.Text;

namespace NATS.Client.Core.Tests;

public class RequestReplyTest
{
    private readonly ITestOutputHelper _output;

    public RequestReplyTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Simple_request_reply_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2);
        });

        for (var i = 0; i < 10; i++)
        {
            var rep = await nats.RequestAsync<int, int>("foo", i);
            Assert.Equal(i * 2, rep?.Data);
        }

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_command_timeout_test()
    {
        await using var server = NatsServer.Start();

        // Request timeout as default timeout
        {
            await using var nats = server.CreateClientConnection(NatsOptions.Default with
            {
                RequestTimeout = TimeSpan.FromSeconds(1),
            });

            var reply = await nats.RequestAsync<int, int>("foo", 0);
            Assert.Null(reply);
        }

        // Cancellation token usage
        {
            await using var nats = server.CreateClientConnection();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await nats.RequestAsync<int, int>("foo", 0, cancellationToken: cts.Token);
            });
        }
    }

    [Fact]
    public async Task Request_reply_many_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            // Start-up timeout
            if (msg.Data == 2)
            {
                await Task.Delay(2_000);
                await msg.ReplyAsync(msg.Data * 2);
            }

            // Idle timeout
            else if (msg.Data == 3)
            {
                await msg.ReplyAsync(msg.Data * 2);
                await Task.Delay(100);
                await msg.ReplyAsync(msg.Data * 3);
                await Task.Delay(5_000);
                await msg.ReplyAsync(msg.Data * 4);
            }

            // Overall timeout
            else if (msg.Data == 4)
            {
                await msg.ReplyAsync(msg.Data * 2);
                await msg.ReplyAsync(msg.Data * 3);
            }

            // Sentinel
            else
            {
                await msg.ReplyAsync(msg.Data * 2);
                await msg.ReplyAsync(msg.Data * 3);
                await msg.ReplyAsync(msg.Data * 4);
                await msg.ReplyAsync<int?>(null); // sentinel
            }
        });

        // Sentinel
        {
            var results = new[] { 2, 3, 4 };
            var count = 0;
            await foreach (var msg in nats.RequestManyAsync<int, int?>("foo", 1))
            {
                Assert.Equal(results[count++], msg.Data);
            }

            Assert.Equal(3, count);
        }

        // Max Count
        {
            var results = new[] { 2, 3, 4 };
            var count = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var opts = new NatsSubOpts { MaxMsgs = 2 };
            await using var rep = await nats.RequestSubAsync<int, int>("foo", 1, replyOpts: opts);
            await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
            {
                Assert.Equal(results[count++], msg.Data);
            }

            Assert.Equal(2, count);
            Assert.Equal(NatsSubEndReason.MaxMsgs, rep.EndReason);
        }

        // Start-up Timeout
        {
            var count = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var opts = new NatsSubOpts { StartUpTimeout = TimeSpan.FromSeconds(1) };
            await using var rep = await nats.RequestSubAsync<int, int>("foo", 2, replyOpts: opts);
            await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
            {
                count++;
            }

            Assert.Equal(0, count);
            Assert.Equal(NatsSubEndReason.StartUpTimeout, rep.EndReason);
        }

        // Idle Timeout
        {
            var results = new[] { 6, 9 };
            var count = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var opts = new NatsSubOpts { IdleTimeout = TimeSpan.FromSeconds(4) };
            await using var rep = await nats.RequestSubAsync<int, int>("foo", 3, replyOpts: opts);
            await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
            {
                Assert.Equal(results[count++], msg.Data);
            }

            Assert.Equal(2, count);
            Assert.Equal(NatsSubEndReason.IdleTimeout, rep.EndReason);
        }

        // Overall Timeout
        {
            var results = new[] { 8, 12 };
            var count = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var opts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(4) };
            await using var rep = await nats.RequestSubAsync<int, int>("foo", 4, replyOpts: opts);
            await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
            {
                Assert.Equal(results[count++], msg.Data);
            }

            Assert.Equal(2, count);
            Assert.Equal(NatsSubEndReason.Timeout, rep.EndReason);
        }

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_binary_test()
    {
        static ReadOnlySequence<byte> ToSeq(string input)
        {
            return new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(input));
        }

        static string ToStr(ReadOnlyMemory<byte> input)
        {
            return Encoding.ASCII.GetString(input.Span);
        }

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        await using var sub = await nats.SubscribeAsync("foo");
        var reg = sub.Register(async m =>
        {
            if (ToStr(m.Data) == "1")
            {
                await m.ReplyAsync(payload: ToSeq("qw"));
                await m.ReplyAsync(payload: ToSeq("er"));
                await m.ReplyAsync(payload: ToSeq("ty"));
                await m.ReplyAsync(payload: default); // sentinel
            }
        });

        var writer = new ArrayBufferWriter<byte>();
        await foreach (var msg in nats.RequestManyAsync("foo", ToSeq("1")))
        {
            writer.Write(msg.Data.Span);
        }

        var buffer = ToStr(writer.WrittenMemory);
        Assert.Equal("qwerty", buffer);

        await sub.DisposeAsync();
        await reg;
    }
}
