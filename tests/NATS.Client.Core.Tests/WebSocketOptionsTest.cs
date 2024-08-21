using System.Net;
using Microsoft.Extensions.Logging;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core.Tests;

public class WebSocketOptionsTest
{
    private readonly List<string> _logs = new();

    // Modeled after similar test in SendBufferTest.cs which also uses the MockServer.
    [Fact]
    public async Task MockWebsocketServer_PubSubWithCancelAndReconnect_ShouldCallbackTwice()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        List<string> pubs = new();
        await using var server = new MockServer(
            handler: (client, cmd) =>
            {
                if (cmd.Name == "PUB")
                {
                    lock (pubs)
                        pubs.Add($"PUB {cmd.Subject}");
                }

                if (cmd is { Name: "PUB", Subject: "close" })
                {
                    client.Close();
                }

                return Task.CompletedTask;
            },
            Log,
            info: $"{{\"max_payload\":{1024 * 4}}}",
            cancellationToken: cts.Token);

        await using var wsServer = new WebSocketMockServer(
            server.Url,
            connectHandler: (httpContext) =>
            {
                return true;
            },
            Log,
            cts.Token);

        Log("__________________________________");

        var testLogger = new InMemoryTestLoggerFactory(LogLevel.Warning, m =>
        {
            Log($"[NC] {m.Message}");
        });

        var tokenCount = 0;
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = wsServer.WebSocketUrl,
            LoggerFactory = testLogger,
            ConfigureWebSocketOpts = (serverUri, clientWsOpts, ct) =>
            {
                tokenCount++;
                Log($"[C] ConfigureWebSocketOpts {serverUri}, accessToken TOKEN_{tokenCount}");
                clientWsOpts.SetRequestHeader("authorization", $"Bearer TOKEN_{tokenCount}");
                return ValueTask.CompletedTask;
            },
        });

        Log($"[C] connect {server.Url}");
        await nats.ConnectAsync();

        Log($"[C] ping");
        var rtt = await nats.PingAsync(cts.Token);
        Log($"[C] ping rtt={rtt}");

        Log($"[C] publishing x1...");
        await nats.PublishAsync("x1", "x", cancellationToken: cts.Token);

        // we will close the connection in mock server when we receive subject "close"
        Log($"[C] publishing close (4KB)...");
        var pubTask = nats.PublishAsync("close", new byte[1024 * 4], cancellationToken: cts.Token).AsTask();

        await pubTask.WaitAsync(cts.Token);

        for (var i = 1; i <= 10; i++)
        {
            try
            {
                await nats.PingAsync(cts.Token);
                break;
            }
            catch (OperationCanceledException)
            {
                if (i == 10)
                    throw;
                await Task.Delay(10 * i, cts.Token);
            }
        }

        Log($"[C] publishing x2...");
        await nats.PublishAsync("x2", "x", cancellationToken: cts.Token);

        Log($"[C] flush...");
        await nats.PingAsync(cts.Token);

        // Look for logs like the following:
        // [WS] Received WebSocketRequest with authorization header: Bearer TOKEN_2
        var tokens = GetLogs().Where(l => l.Contains("Bearer")).ToList();
        Assert.Equal(2, tokens.Count);
        var token = tokens.Where(t => t.Contains("TOKEN_1"));
        Assert.Single(token);
        token = tokens.Where(t => t.Contains("TOKEN_2"));
        Assert.Single(token);

        lock (pubs)
        {
            Assert.Equal(3, pubs.Count);
            Assert.Equal("PUB x1", pubs[0]);
            Assert.Equal("PUB close", pubs[1]);
            Assert.Equal("PUB x2", pubs[2]);
        }
    }

    [Fact]
    public async Task WebSocketRespondsWithHttpError_ShouldThrowNatsException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = new MockServer(
            handler: (client, cmd) =>
            {
                return Task.CompletedTask;
            },
            Log,
            info: $"{{\"max_payload\":{1024 * 4}}}",
            cancellationToken: cts.Token);

        await using var wsServer = new WebSocketMockServer(
            server.Url,
            connectHandler: (httpContext) =>
            {
                httpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return false; // reject connection
            },
            Log,
            cts.Token);

        Log("__________________________________");

        var testLogger = new InMemoryTestLoggerFactory(LogLevel.Warning, m =>
        {
            Log($"[NC] {m.Message}");
        });

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = wsServer.WebSocketUrl,
            LoggerFactory = testLogger,
        });

        Log($"[C] connect {server.Url}");

        // expect: NATS.Client.Core.NatsException : can not connect uris: ws://127.0.0.1:5004
        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
    }

    [Fact]
    public async Task HttpErrorDuringReconnect_ShouldContinueToReconnect()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var server = new MockServer(
            handler: (client, cmd) =>
            {
                if (cmd is { Name: "PUB", Subject: "close" })
                {
                    client.Close();
                }

                return Task.CompletedTask;
            },
            Log,
            info: $"{{\"max_payload\":{1024 * 4}}}",
            cancellationToken: cts.Token);

        var tokenCount = 0;

        await using var wsServer = new WebSocketMockServer(
            server.Url,
            connectHandler: (httpContext) =>
            {
                var token = httpContext.Request.Headers.Authorization;
                if (token.Contains("Bearer TOKEN_1") || token.Contains("Bearer TOKEN_4"))
                {
                    return true;
                }
                else
                {
                    httpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return false; // reject connection
                }
            },
            Log,
            cts.Token);

        Log("__________________________________");

        var testLogger = new InMemoryTestLoggerFactory(LogLevel.Warning, m =>
        {
            Log($"[NC] {m.Message}");
        });

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = wsServer.WebSocketUrl,
            LoggerFactory = testLogger,
            ConfigureWebSocketOpts = (serverUri, clientWsOpts, ct) =>
            {
                tokenCount++;
                Log($"[C] ConfigureWebSocketOpts {serverUri}, accessToken TOKEN_{tokenCount}");
                clientWsOpts.SetRequestHeader("authorization", $"Bearer TOKEN_{tokenCount}");
                return ValueTask.CompletedTask;
            },
        });

        Log($"[C] connect {server.Url}");

        // close connection and trigger reconnect
        Log($"[C] publishing close ...");
        await nats.PublishAsync("close", "x", cancellationToken: cts.Token);

        for (var i = 1; i <= 10; i++)
        {
            try
            {
                await nats.PingAsync(cts.Token);
                break;
            }
            catch (OperationCanceledException)
            {
                if (i == 10)
                    throw;
                await Task.Delay(100 * i, cts.Token);
            }
        }

        Log($"[C] publishing reconnected");
        await nats.PublishAsync("reconnected", "rc", cancellationToken: cts.Token);
        await Task.Delay(100); // short delay to allow log to be collected for reconnect

        // Expect to see in logs:
        // 1st callback and TOKEN_1
        // Initial Connect
        // 2nd callback with rejected TOKEN_2
        // NC reconnect
        // 3rd callback with rejected TOKEN_3
        // NC reconnect
        // 4th callback with good TOKEN_4
        // Successful Publish after reconnect

        // 4 tokens
        var logs = GetLogs();
        var tokens = logs.Where(l => l.Contains("Bearer")).ToList();
        Assert.Equal(4, tokens.Count);
        Assert.Single(tokens.Where(t => t.Contains("TOKEN_1")));
        Assert.Single(tokens.Where(t => t.Contains("TOKEN_2")));
        Assert.Single(tokens.Where(t => t.Contains("TOKEN_3")));
        Assert.Single(tokens.Where(t => t.Contains("TOKEN_4")));

        // 2 errors in NATS.Client triggering the reconnect
        var failures = logs.Where(l => l.Contains("[NC] Failed to connect NATS"));
        Assert.Equal(2, failures.Count());

        // 2 connects in MockServer
        var connects = logs.Where(l => l.Contains("RCV CONNECT"));
        Assert.Equal(2, failures.Count());

        // 1 reconnect in MockServer
        var reconnectPublish = logs.Where(l => l.Contains("RCV PUB reconnected"));
        Assert.Single(reconnectPublish);
    }

    [Fact]
    public async Task ExceptionThrownInCallback_ShouldThrowNatsException()
    {
        // void Log(string m) => TmpFileLogger.Log(m);
        List<string> logs = new();
        void Log(string m)
        {
            lock (logs)
                logs.Add(m);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var testLogger = new InMemoryTestLoggerFactory(LogLevel.Warning, m =>
        {
            Log($"[NC] {m.Message}");
        });

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = "ws://localhost:1234",
            LoggerFactory = testLogger,
            ConfigureWebSocketOpts = (serverUri, clientWsOpts, ct) =>
            {
                throw new Exception("Error in callback");
            },
        });

        // expect: NATS.Client.Core.NatsException : can not connect uris: ws://localhost:1234
        var exception = await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
    }

    private void Log(string m)
    {
        lock (_logs)
            _logs.Add(m);
    }

    private List<string> GetLogs()
    {
        lock (_logs)
            return _logs.ToList();
    }
}
