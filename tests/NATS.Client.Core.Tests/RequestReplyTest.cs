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
            await using var nats = server.CreateClientConnection(NatsOpts.Default with
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
        const int msgs = 10;

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            for (var i = 0; i < msgs; i++)
            {
                await msg.ReplyAsync(msg.Data * i);
            }

            await msg.ReplyAsync<int?>(null); // stop iteration with a sentinel
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        const int data = 2;
        var results = Enumerable.Range(0, msgs).Select(x => x * data).ToArray();
        var count = 0;
        await foreach (var msg in nats.RequestManyAsync<int, int?>("foo", data, cancellationToken: cts.Token))
        {
            Assert.Equal(results[count++], msg.Data);
        }

        Assert.Equal(results.Length, count);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_many_test_overall_timeout()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2);
            await msg.ReplyAsync(msg.Data * 3);
        });

        var results = new[] { 8, 12 };
        var count = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var opts = new NatsSubOpts { Timeout = TimeSpan.FromSeconds(4) };
        await using var rep =
            await nats.RequestSubAsync<int, int>("foo", 4, replyOpts: opts, cancellationToken: cts.Token);
        await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
        {
            Assert.Equal(results[count++], msg.Data);
        }

        Assert.Equal(2, count);
        Assert.Equal(NatsSubEndReason.Timeout, ((NatsSubBase)rep).EndReason);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_many_test_idle_timeout()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2);
            await Task.Delay(100);
            await msg.ReplyAsync(msg.Data * 3);
            await Task.Delay(5_000);
            await msg.ReplyAsync(msg.Data * 4);
        });

        var results = new[] { 6, 9 };
        var count = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var opts = new NatsSubOpts { IdleTimeout = TimeSpan.FromSeconds(3) };
        await using var rep =
            await nats.RequestSubAsync<int, int>("foo", 3, replyOpts: opts, cancellationToken: cts.Token);
        await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
        {
            Assert.Equal(results[count++], msg.Data);
        }

        Assert.Equal(2, count);
        Assert.Equal(NatsSubEndReason.IdleTimeout, ((NatsSubBase)rep).EndReason);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_many_test_start_up_timeout()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await Task.Delay(2_000);
            await msg.ReplyAsync(msg.Data * 2);
        });

        var count = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var opts = new NatsSubOpts { StartUpTimeout = TimeSpan.FromSeconds(1) };
        await using var rep =
            await nats.RequestSubAsync<int, int>("foo", 2, replyOpts: opts, cancellationToken: cts.Token);
        await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
        {
            count++;
        }

        Assert.Equal(0, count);
        Assert.Equal(NatsSubEndReason.StartUpTimeout, ((NatsSubBase)rep).EndReason);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_many_test_max_count()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2);
            await msg.ReplyAsync(msg.Data * 3);
            await msg.ReplyAsync(msg.Data * 4);
            await msg.ReplyAsync<int?>(null); // sentinel
        });

        var results = new[] { 2, 3, 4 };
        var count = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var opts = new NatsSubOpts { MaxMsgs = 2 };
        await using var rep =
            await nats.RequestSubAsync<int, int>("foo", 1, replyOpts: opts, cancellationToken: cts.Token);
        await foreach (var msg in rep.Msgs.ReadAllAsync(cts.Token))
        {
            Assert.Equal(results[count++], msg.Data);
        }

        Assert.Equal(2, count);
        Assert.Equal(NatsSubEndReason.MaxMsgs, ((NatsSubBase)rep).EndReason);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task Request_reply_many_test_sentinel()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2);
            await msg.ReplyAsync(msg.Data * 3);
            await msg.ReplyAsync(msg.Data * 4);
            await msg.ReplyAsync<int?>(null); // sentinel
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var results = new[] { 2, 3, 4 };
        var count = 0;
        await foreach (var msg in nats.RequestManyAsync<int, int?>("foo", 1, cancellationToken: cts.Token))
        {
            Assert.Equal(results[count++], msg.Data);
        }

        Assert.Equal(3, count);

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

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();
        await using var sub = await nats.SubscribeAsync("foo", cancellationToken: cts.Token);
        var reg = sub.Register(async m =>
        {
            if (ToStr(m.Data) == "1")
            {
                await m.ReplyAsync(payload: ToSeq("qw"), cancellationToken: cts.Token);
                await m.ReplyAsync(payload: ToSeq("er"), cancellationToken: cts.Token);
                await m.ReplyAsync(payload: ToSeq("ty"), cancellationToken: cts.Token);
                await m.ReplyAsync(payload: default, cancellationToken: cts.Token); // sentinel
            }
        });

        var writer = new ArrayBufferWriter<byte>();
        await foreach (var msg in nats.RequestManyAsync("foo", ToSeq("1"), cancellationToken: cts.Token))
        {
            writer.Write(msg.Data.Span);
        }

        var buffer = ToStr(writer.WrittenMemory);
        Assert.Equal("qwerty", buffer);

        await sub.DisposeAsync();
        await reg;
    }
}
