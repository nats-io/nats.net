using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

/// <summary>
/// These tests use a raw TcpListener to send crafted protocol data
/// that a malicious server could send to crash the client via OOM.
/// </summary>
public class ProtocolParserSizeCheckTest(ITestOutputHelper output)
{
    /// <summary>
    /// MSG with payload size exceeding server's max_payload must not
    /// cause an unbounded allocation (previously would call ReadAtLeastAsync(2147483647)).
    /// </summary>
    [Fact]
    public async Task Msg_with_payload_exceeding_max_payload_does_not_oom()
    {
        var logFactory = new InMemoryTestLoggerFactory(LogLevel.Error, m => output.WriteLine($"[LOG] {m.Message}"));
        await using var server = new FakeServer(output);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logFactory });
        await nats.ConnectRetryAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await nats.SubscribeCoreAsync<int>("foo", cancellationToken: cts.Token);
        await server.WaitForSubAsync("foo");

        // Send a MSG claiming payload of 2GB — far exceeds max_payload
        await server.SendRawAsync("MSG foo 1 2147483647\r\n");

        await new Func<IReadOnlyList<InMemoryTestLoggerFactory.LogMessage>>(() => logFactory.Logs)
            .ShouldWithRetryAsync(
                m => m.LogLevel == LogLevel.Error
                     && m.Exception is NatsProtocolViolationException
                     && m.Exception.Message.Contains("max allowed size"),
                "MSG with oversized payload should be rejected");
    }

    /// <summary>
    /// MSG with negative payload length must be rejected.
    /// </summary>
    [Fact]
    public async Task Msg_with_negative_payload_length_does_not_oom()
    {
        var logFactory = new InMemoryTestLoggerFactory(LogLevel.Error, m => output.WriteLine($"[LOG] {m.Message}"));
        await using var server = new FakeServer(output);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logFactory });
        await nats.ConnectRetryAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await nats.SubscribeCoreAsync<int>("foo", cancellationToken: cts.Token);
        await server.WaitForSubAsync("foo");

        await server.SendRawAsync("MSG foo 1 -1\r\n");

        await new Func<IReadOnlyList<InMemoryTestLoggerFactory.LogMessage>>(() => logFactory.Logs)
            .ShouldWithRetryAsync(
                m => m.LogLevel == LogLevel.Error
                     && m.Exception is NatsProtocolViolationException
                     && m.Exception.Message.Contains("Negative"),
                "MSG with negative payload length should be rejected");
    }

    /// <summary>
    /// HMSG with totalLength less than headersLength must be rejected.
    /// Previously only protected by Debug.Assert (stripped in Release).
    /// </summary>
    [Fact]
    public async Task Hmsg_with_total_less_than_headers_does_not_oom()
    {
        var logFactory = new InMemoryTestLoggerFactory(LogLevel.Error, m => output.WriteLine($"[LOG] {m.Message}"));
        await using var server = new FakeServer(output);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logFactory });
        await nats.ConnectRetryAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await nats.SubscribeCoreAsync<int>("foo", cancellationToken: cts.Token);
        await server.WaitForSubAsync("foo");

        // headersLength=100, totalLength=10 — impossible
        await server.SendRawAsync("HMSG foo 1 100 10\r\n");

        await new Func<IReadOnlyList<InMemoryTestLoggerFactory.LogMessage>>(() => logFactory.Logs)
            .ShouldWithRetryAsync(
                m => m.LogLevel == LogLevel.Error
                     && m.Exception is NatsProtocolViolationException
                     && m.Exception.Message.Contains("less than headers"),
                "HMSG with total < headers should be rejected");
    }

    /// <summary>
    /// HMSG with totalLength exceeding max_payload must be rejected.
    /// </summary>
    [Fact]
    public async Task Hmsg_with_total_exceeding_max_payload_does_not_oom()
    {
        var logFactory = new InMemoryTestLoggerFactory(LogLevel.Error, m => output.WriteLine($"[LOG] {m.Message}"));
        await using var server = new FakeServer(output);

        await server.Ready;
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logFactory });
        await nats.ConnectRetryAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await nats.SubscribeCoreAsync<int>("foo", cancellationToken: cts.Token);
        await server.WaitForSubAsync("foo");

        // headersLength=10, totalLength=2147483647 — exceeds 1MB max_payload
        await server.SendRawAsync("HMSG foo 1 10 2147483647\r\n");

        await new Func<IReadOnlyList<InMemoryTestLoggerFactory.LogMessage>>(() => logFactory.Logs)
            .ShouldWithRetryAsync(
                m => m.LogLevel == LogLevel.Error
                     && m.Exception is NatsProtocolViolationException
                     && m.Exception.Message.Contains("max allowed size"),
                "HMSG exceeding max_payload should be rejected");
    }

    /// <summary>
    /// A protocol violation exits the read loop cleanly (not in a spin loop).
    /// The connection will attempt to reconnect to other servers.
    /// </summary>
    [Fact]
    public async Task Protocol_violation_exits_read_loop_cleanly()
    {
        var logFactory = new InMemoryTestLoggerFactory(LogLevel.Error, m => output.WriteLine($"[LOG] {m.Message}"));
        await using var server = new FakeServer(output);

        // MaxReconnectRetry=0 because FakeServer only accepts one connection
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, LoggerFactory = logFactory, MaxReconnectRetry = 0 });
        await nats.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await nats.SubscribeCoreAsync<int>("foo", cancellationToken: cts.Token);
        await server.WaitForSubAsync("foo");

        // Send a malicious MSG with negative payload
        await server.SendRawAsync("MSG foo 1 -1\r\n");

        // The violation should be logged exactly once (not in a loop), then connection drops
        await new Func<IReadOnlyList<InMemoryTestLoggerFactory.LogMessage>>(() => logFactory.Logs)
            .ShouldWithRetryAsync(
                m => m.LogLevel == LogLevel.Error
                     && m.Exception is NatsProtocolViolationException
                     && m.Exception.Message.Contains("Negative"),
                "protocol violation should be logged");

        // Give a moment for any spin to manifest, then verify no error flood.
        // Expect a small number (read loop + reconnect loop each log once) but not hundreds.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var violationCount = logFactory.Logs.Count(m => m.Exception is NatsProtocolViolationException);
        violationCount.Should().BeLessThanOrEqualTo(3, "the error should be logged a few times, not in a spin loop");
    }

    /// <summary>
    /// Sanity check: a valid MSG with normal payload still works after the fixes.
    /// </summary>
    [Fact]
    public async Task Valid_msg_still_works()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = new MockServer(
            handler: (client, cmd) =>
            {
                if (cmd.Name == "SUB")
                {
                    client.SendMsg(cmd.Subject, payload: "hello");
                }

                return Task.CompletedTask;
            },
            logger: m => output.WriteLine(m),
            cancellationToken: cts.Token);

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        await foreach (var msg in nats.SubscribeAsync<string>("foo", cancellationToken: cts.Token))
        {
            msg.Data.Should().Be("hello");
            break;
        }
    }

    /// <summary>
    /// Sanity check: a valid HMSG with headers and payload still works after the fixes.
    /// Uses raw wire protocol to ensure correct byte counts.
    /// </summary>
    [Fact]
    public async Task Valid_hmsg_still_works()
    {
        await using var server = new FakeServer(output);

        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sub = await nats.SubscribeCoreAsync<byte[]>("foo", cancellationToken: cts.Token);
        await server.WaitForSubAsync("foo");

        // Build a well-formed HMSG:
        // Headers: "NATS/1.0\r\nX-Test: value\r\n\r\n" (28 bytes, including the \r\n\r\n terminator)
        // Payload: "world" (5 bytes)
        // Total: 33 bytes
        var headers = "NATS/1.0\r\nX-Test: value\r\n\r\n";
        var payload = "world";
        var headersLen = headers.Length;
        var totalLen = headersLen + payload.Length;
        await server.SendRawAsync($"HMSG foo 1 {headersLen} {totalLen}\r\n{headers}{payload}\r\n");

        await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
        {
            msg.Headers.Should().NotBeNull();
            msg.Headers!["X-Test"].ToString().Should().Be("value");
            break;
        }
    }

    /// <summary>
    /// A minimal fake NATS server that does the handshake then exposes raw send.
    /// </summary>
    private sealed class FakeServer : IAsyncDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
        private readonly TaskCompletionSource _accepted = new();
        private readonly TaskCompletionSource _ready = new();
        private readonly Dictionary<string, TaskCompletionSource> _subWaiters = new();
        private TcpClient? _tcpClient;
        private StreamWriter? _writer;
        private Task? _readLoop;
        private bool _protocolDump;

        public FakeServer(ITestOutputHelper output, string info = "{\"max_payload\":1048576}")
        {
            _output = output;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // Accept one client and perform handshake
            _readLoop = Task.Run(async () => await AcceptAndServeAsync(info), _cts.Token);
        }

        public int Port { get; }

        public Task Ready => _ready.Task;

        public string Url => $"127.0.0.1:{Port}";

        /// <summary>
        /// Enable protocol dump — all sent and received data is logged to test output.
        /// Call before ConnectAsync.
        /// </summary>
        public FakeServer EnableProtocolDump()
        {
            _protocolDump = true;
            return this;
        }

        public async Task SendRawAsync(string data)
        {
            await _accepted.Task.ConfigureAwait(false);
            if (_protocolDump)
            {
                var preview = data.Length > 200 ? data.Substring(0, 200) + $"...({data.Length} chars)" : data;
                _output.WriteLine($"[S] SND: {preview.Replace("\r", "\\r").Replace("\n", "\\n")}");
            }

            await _writer!.WriteAsync(data);
            await _writer.FlushAsync();
        }

        public Task WaitForSubAsync(string subject)
        {
            lock (_subWaiters)
            {
                if (_subWaiters.TryGetValue(subject, out var existing) && existing.Task.IsCompleted)
                    return Task.CompletedTask;

                var tcs = new TaskCompletionSource();
                _subWaiters[subject] = tcs;
                return tcs.Task;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            _tcpClient?.Dispose();

            if (_readLoop != null)
            {
                try
                {
                    await _readLoop.WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }

        private async Task AcceptAndServeAsync(string info)
        {
            _ready.SetResult();
            _tcpClient = await _listener.AcceptTcpClientAsync();
            var stream = _tcpClient.GetStream();
            var encoding = Encoding.GetEncoding(28591);
            _writer = new StreamWriter(stream, encoding) { AutoFlush = false };
            var reader = new StreamReader(stream, encoding);

            // Send INFO
            var infoLine = $"INFO {info}\r\n";
            if (_protocolDump)
                _output.WriteLine($"[S] SND: {infoLine.Replace("\r", "\\r").Replace("\n", "\\n")}");
            await _writer.WriteAsync(infoLine);
            await _writer.FlushAsync();

            // Read lines: respond to PING, track SUB
            while (!_cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                    break;

                _output.WriteLine($"[S] RCV: {line}");
                if (_protocolDump)
                    _output.WriteLine($"[S] RCV (dump): {line}");

                if (line.StartsWith("PING"))
                {
                    await _writer.WriteAsync("PONG\r\n");
                    await _writer.FlushAsync();

                    if (!_accepted.Task.IsCompleted)
                        _accepted.TrySetResult();
                }
                else if (line.StartsWith("SUB"))
                {
                    // SUB <subject> [queue] <sid>
                    var parts = line.Split(' ');
                    var subject = parts[1];

                    lock (_subWaiters)
                    {
                        if (_subWaiters.TryGetValue(subject, out var tcs))
                            tcs.TrySetResult();
                        else
                            _subWaiters[subject] = new TaskCompletionSource();

                        // Mark as completed for late callers
                        if (!_subWaiters[subject].Task.IsCompleted)
                            _subWaiters[subject].TrySetResult();
                    }
                }
            }
        }
    }
}
