using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Primitives;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core.Tests;

public class WebSocketOptionsTest
{
    [Fact]
    public async Task Exception_in_callback_throws_nats_exception()
    {
        await using var server = await NatsServer.StartAsync(
            new NullOutputHelper(),
            new NatsServerOptsBuilder()
                .UseTransport(TransportType.WebSocket)
                .Build());

        var clientOpts = server.ClientOpts(NatsOpts.Default with { Name = "ws-test-client" });
        clientOpts = clientOpts with
        {
            WebSocketOpts = new NatsWebSocketOpts
            {
                ConfigureClientWebSocketOptions = (_, _, _)
                    => throw new Exception("Error in callback"),
            },
        };
        await using var nats = new NatsConnection(clientOpts);

        await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());
    }

    [Fact]
    public async Task Request_headers_are_correct()
    {
        var server = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        server.Start();

        var port = ((IPEndPoint)server.LocalEndpoint).Port;

        var headers = new List<string>();
        var serverTask = Task.Run(async () =>
        {
            using var client = await server.AcceptTcpClientAsync();
            var stream = client.GetStream();
            using var sr = new StreamReader(stream);

            while (true)
            {
                var line = await sr.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    return;

                headers.Add(line);
            }
        });

        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = $"ws://127.0.0.1:{port}",
            WebSocketOpts = new NatsWebSocketOpts
            {
                RequestHeaders = new Dictionary<string, StringValues> { { "Header1", "Header1" }, { "Header2", new StringValues(["Header2.1", "Header2.2"]) }, },
                ConfigureClientWebSocketOptions = (_, clientWsOpts, _) =>
                {
                    clientWsOpts.SetRequestHeader("Header3", "Header3");
                    return default;
                },
            },
        });

        // not connecting to an actual nats server so this throws
        await Assert.ThrowsAsync<NatsException>(async () => await nats.ConnectAsync());

        await serverTask;

        Assert.Contains("Header1: Header1", headers);
        Assert.Contains("Header2: Header2.1,Header2.2", headers);
        Assert.Contains("Header3: Header3", headers);
    }
}
