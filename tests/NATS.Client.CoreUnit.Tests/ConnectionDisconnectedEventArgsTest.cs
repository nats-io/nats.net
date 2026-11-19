using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NATS.Client.CoreUnit.Tests;

public class ConnectionDisconnectedEventArgsTest
{
    [Fact]
    public async Task ConnectionDisconnected_event_args_should_expose_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var closeSocket = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = Task.Run(
                async () =>
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                    var stream = client.GetStream();
                    var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
                    var reader = new StreamReader(stream, Encoding.ASCII);

                    await writer.WriteLineAsync("INFO {\"max_payload\":1048576}");
                    while (await reader.ReadLineAsync(cancellationToken) is { } line && !line.StartsWith("PING"))
                    {
                    }

                    await writer.WriteLineAsync("PONG");
                    await closeSocket.Task;
                },
                cancellationToken);

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await using var nats = new NatsConnection(new NatsOpts { Url = $"127.0.0.1:{port}" });

            var disconnected = new TaskCompletionSource<NatsEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            nats.ConnectionDisconnected += (_, args) =>
            {
                disconnected.TrySetResult(args);
                return default;
            };

            await nats.ConnectAsync();
            closeSocket.SetResult();
            await server;

            var args = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            args.Exception.Should().NotBeNull();
            args.Exception!.Message.Should().Be("Socket has been closed.");
            args.Message.Should().Contain(port.ToString());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void NatsEventArgs_exception_is_null_when_no_cause_is_known()
    {
        var args = new NatsEventArgs("nats://localhost:4222");

        args.Exception.Should().BeNull();
    }

    [Fact]
    public void NatsEventArgs_exposes_exception_passed_to_constructor()
    {
        var error = new InvalidOperationException("boom");
        var args = new NatsEventArgs("nats://localhost:4222", error);

        args.Message.Should().Be("nats://localhost:4222");
        args.Exception.Should().BeSameAs(error);
    }
}
