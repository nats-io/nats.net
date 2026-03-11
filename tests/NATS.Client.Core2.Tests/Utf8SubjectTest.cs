using System.Text;
using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core.Tests;

public class Utf8SubjectMockServerTest
{
    [Fact]
    public async Task Utf8_subject_is_decoded_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // UTF-8 subject with multi-byte characters
        var utf8Subject = "test.ñoño.日本語";

        await using var server = new MockServer(
            async (client, cmd) =>
            {
                if (cmd is { Name: "SUB", Subject: ">" })
                {
                    // Build raw MSG protocol line with UTF-8 encoded subject bytes.
                    // MockServer uses ISO 8859-1 so we write raw bytes to the stream
                    // to ensure multi-byte UTF-8 characters are sent correctly.
                    var payload = "hello"u8.ToArray();
                    var msgLine = $"MSG {utf8Subject} {cmd.Sid} {payload.Length}\r\n";
                    var msgLineBytes = Encoding.UTF8.GetBytes(msgLine);

                    await client.Writer.FlushAsync().ConfigureAwait(false);
                    var stream = client.Writer.BaseStream;
                    await stream.WriteAsync(msgLineBytes, 0, msgLineBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                    var crlf = "\r\n"u8.ToArray();
                    await stream.WriteAsync(crlf, 0, crlf.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            },
            cancellationToken: cts.Token);

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        await foreach (var msg in nats.SubscribeAsync<string>(">", cancellationToken: cts.Token))
        {
            Assert.Equal(utf8Subject, msg.Subject);
            break;
        }
    }

    [Fact]
    public async Task Emoji_subject_and_reply_to_are_decoded_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var emojiSubject = "events.🔥.📬";
        var emojiReplyTo = "_INBOX.🎉.reply";

        await using var server = new MockServer(
            async (client, cmd) =>
            {
                if (cmd is { Name: "SUB", Subject: ">" })
                {
                    var payload = "data"u8.ToArray();

                    // MSG <subject> <sid> <reply-to> <#bytes>\r\n[payload]\r\n
                    var msgLine = $"MSG {emojiSubject} {cmd.Sid} {emojiReplyTo} {payload.Length}\r\n";
                    var msgLineBytes = Encoding.UTF8.GetBytes(msgLine);

                    await client.Writer.FlushAsync().ConfigureAwait(false);
                    var stream = client.Writer.BaseStream;
                    await stream.WriteAsync(msgLineBytes, 0, msgLineBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                    var crlf = "\r\n"u8.ToArray();
                    await stream.WriteAsync(crlf, 0, crlf.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            },
            cancellationToken: cts.Token);

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        await foreach (var msg in nats.SubscribeAsync<string>(">", cancellationToken: cts.Token))
        {
            Assert.Equal(emojiSubject, msg.Subject);
            Assert.Equal(emojiReplyTo, msg.ReplyTo);
            break;
        }
    }

    [Fact]
    public async Task Utf8_subject_with_hmsg_is_decoded_correctly_mock()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var utf8Subject = "hmsg.über.café";
        var utf8ReplyTo = "_INBOX.naïve.reply";
        var headers = "NATS/1.0\r\nX-Test: value\r\n\r\n";
        var payload = "payload";

        await using var server = new MockServer(
            async (client, cmd) =>
            {
                if (cmd is { Name: "SUB", Subject: ">" })
                {
                    var headersBytes = Encoding.UTF8.GetBytes(headers);
                    var payloadBytes = Encoding.UTF8.GetBytes(payload);
                    var totalLen = headersBytes.Length + payloadBytes.Length;

                    // HMSG <subject> <sid> <reply-to> <#header-bytes> <#total-bytes>\r\n[headers]\r\n\r\n[payload]\r\n
                    var msgLine = $"HMSG {utf8Subject} {cmd.Sid} {utf8ReplyTo} {headersBytes.Length} {totalLen}\r\n";
                    var msgLineBytes = Encoding.UTF8.GetBytes(msgLine);

                    await client.Writer.FlushAsync().ConfigureAwait(false);
                    var stream = client.Writer.BaseStream;
                    await stream.WriteAsync(msgLineBytes, 0, msgLineBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(headersBytes, 0, headersBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length).ConfigureAwait(false);
                    var crlf = "\r\n"u8.ToArray();
                    await stream.WriteAsync(crlf, 0, crlf.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            },
            cancellationToken: cts.Token);

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        await foreach (var msg in nats.SubscribeAsync<string>(">", cancellationToken: cts.Token))
        {
            Assert.Equal(utf8Subject, msg.Subject);
            Assert.Equal(utf8ReplyTo, msg.ReplyTo);
            Assert.Equal("value", msg.Headers?["X-Test"]);
            break;
        }
    }
}

[Collection("nats-server")]
public class Utf8SubjectServerTest
{
    private readonly NatsServerFixture _server;

    public Utf8SubjectServerTest(NatsServerFixture server) => _server = server;

    [Fact]
    public async Task Utf8_subject_pub_sub_with_real_server()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });

        var subject = "test.café.🔥";
        var sync = 0;
        NatsMsg<string> received = default;

        var sub = Task.Run(async () =>
        {
            await foreach (var msg in nats.SubscribeAsync<string>("test.>"))
            {
                if (msg.Subject == "test.sync")
                {
                    Interlocked.Increment(ref sync);
                    continue;
                }

                received = msg;
                break;
            }
        });

        await Retry.Until(
            reason: "subscription is ready",
            condition: () => Volatile.Read(ref sync) > 0,
            action: async () => await nats.PublishAsync("test.sync"),
            retryDelay: TimeSpan.FromSeconds(1));

        await nats.PublishAsync(subject: subject, data: "hello");
        await sub;

        Assert.Equal(subject, received.Subject);
        Assert.Equal("hello", received.Data);
    }

    [Fact]
    public async Task Utf8_subject_request_reply_with_real_server()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });

        var subject = "svc.ñoño.日本語";
        var sync = 0;

        var responder = Task.Run(async () =>
        {
            await foreach (var msg in nats.SubscribeAsync<string>("svc.>"))
            {
                if (msg.Subject == "svc.sync")
                {
                    Interlocked.Increment(ref sync);
                    continue;
                }

                Assert.Equal(subject, msg.Subject);
                Assert.NotNull(msg.ReplyTo);
                await msg.ReplyAsync("pong");
                break;
            }
        });

        await Retry.Until(
            reason: "responder is ready",
            condition: () => Volatile.Read(ref sync) > 0,
            action: async () => await nats.PublishAsync("svc.sync"),
            retryDelay: TimeSpan.FromSeconds(1));

        var reply = await nats.RequestAsync<string, string>(subject: subject, data: "ping");

        Assert.Equal("pong", reply.Data);

        await responder;
    }
}
