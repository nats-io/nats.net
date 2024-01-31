using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace NATS.Client.TestUtilities;

public class MockServer : IAsyncDisposable
{
    private readonly Action<string> _logger;
    private readonly TcpListener _server;
    private readonly Task _accept;

    public MockServer(
        Func<MockServer, Cmd, Task> handler,
        Action<string> logger,
        CancellationToken cancellationToken)
    {
        _logger = logger;
        _server = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        _server.Start();
        Port = ((IPEndPoint)_server.LocalEndpoint).Port;

        _accept = Task.Run(
            async () =>
            {
                var client = await _server.AcceptTcpClientAsync();

                var stream = client.GetStream();

                var sw = new StreamWriter(stream, Encoding.ASCII);
                await sw.WriteAsync("INFO {}\r\n");
                await sw.FlushAsync();

                var sr = new StreamReader(stream, Encoding.ASCII);

                while (!cancellationToken.IsCancellationRequested)
                {
                    Log($"[S] >>> READ LINE");
                    var line = (await sr.ReadLineAsync())!;

                    if (line.StartsWith("CONNECT"))
                    {
                        Log($"[S] RCV CONNECT");
                    }
                    else if (line.StartsWith("PING"))
                    {
                        Log($"[S] RCV PING");
                        await sw.WriteAsync("PONG\r\n");
                        await sw.FlushAsync();
                        Log($"[S] SND PONG");
                    }
                    else if (line.StartsWith("SUB"))
                    {
                        var m = Regex.Match(line, @"^SUB\s+(?<subject>\S+)");
                        var subject = m.Groups["subject"].Value;
                        Log($"[S] RCV SUB {subject}");
                        await handler(this, new Cmd("SUB", subject, 0));
                    }
                    else if (line.StartsWith("PUB") || line.StartsWith("HPUB"))
                    {
                        var m = Regex.Match(line, @"^(H?PUB)\s+(?<subject>\S+).*?(?<size>\d+)$");
                        var size = int.Parse(m.Groups["size"].Value);
                        var subject = m.Groups["subject"].Value;
                        Log($"[S] RCV PUB {subject} {size}");
                        var read = 0;
                        var buffer = new byte[size];
                        while (read < size)
                        {
                            var received = await stream.ReadAsync(buffer, read, size - read);
                            read += received;
                            Log($"[S] RCV {received} bytes (size={size} read={read})");
                        }

                        await handler(this, new Cmd("PUB", subject, size));
                        await sr.ReadLineAsync();
                    }
                    else
                    {
                        Log($"[S] RCV LINE: {line}");
                    }
                }
            },
            cancellationToken);
    }

    public int Port { get; }

    public string Url => $"127.0.0.1:{Port}";

    public async ValueTask DisposeAsync()
    {
        _server.Stop();
        try
        {
            await _accept;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Log(string m) => _logger(m);

    public record Cmd(string Name, string Subject, int Size);
}
