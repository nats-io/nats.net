using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace NATS.Client.TestUtilities;

public class MockServer : IAsyncDisposable
{
    private readonly Action<string> _logger;
    private readonly TcpListener _server;
    private readonly List<Task> _clients = new();
    private readonly Task _accept;
    private readonly CancellationTokenSource _cts;

    public MockServer(
        Func<Client, Cmd, Task> handler,
        Action<string> logger,
        string info = "{\"max_payload\":1048576}",
        CancellationToken cancellationToken = default)
    {
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = _cts.Token;
        _server = new TcpListener(IPAddress.Parse("127.0.0.1"), 0);
        _server.Start(10);
        Port = ((IPEndPoint)_server.LocalEndpoint).Port;

        _accept = Task.Run(
            async () =>
            {
                var n = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    var tcpClient = await _server.AcceptTcpClientAsync(cancellationToken);
                    var client = new Client(this, tcpClient);
                    n++;
                    Log($"[S] [{n}] New client connected");
                    var stream = tcpClient.GetStream();

                    var sw = new StreamWriter(stream, Encoding.ASCII);
                    await sw.WriteAsync($"INFO {info}\r\n");
                    await sw.FlushAsync();

                    var sr = new StreamReader(stream, Encoding.ASCII);

                    _clients.Add(Task.Run(async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var line = await sr.ReadLineAsync();
                            if (line == null)
                            {
                                // empty read, socket closed
                                return;
                            }

                            if (line.StartsWith("CONNECT"))
                            {
                                Log($"[S] [{n}] RCV CONNECT");
                            }
                            else if (line.StartsWith("PING"))
                            {
                                Log($"[S] [{n}] RCV PING");
                                await sw.WriteAsync("PONG\r\n");
                                await sw.FlushAsync();
                                Log($"[S] [{n}] SND PONG");
                            }
                            else if (line.StartsWith("SUB"))
                            {
                                var m = Regex.Match(line, @"^SUB\s+(?<subject>\S+)");
                                var subject = m.Groups["subject"].Value;
                                Log($"[S] [{n}] RCV SUB {subject}");
                                await handler(client, new Cmd("SUB", subject, 0));
                            }
                            else if (line.StartsWith("PUB") || line.StartsWith("HPUB"))
                            {
                                var m = Regex.Match(line, @"^(H?PUB)\s+(?<subject>\S+).*?(?<size>\d+)$");
                                var size = int.Parse(m.Groups["size"].Value);
                                var subject = m.Groups["subject"].Value;
                                Log($"[S] [{n}] RCV PUB {subject} {size}");
                                await handler(client, new Cmd("PUB", subject, size));
                                var read = 0;
                                var buffer = new char[size];
                                while (read < size)
                                {
                                    var received = await sr.ReadAsync(buffer, read, size - read);
                                    read += received;
                                    Log($"[S] [{n}] RCV {received} bytes (size={size} read={read})");
                                }

                                // Log($"[S] RCV PUB payload: {new string(buffer)}");
                                await sr.ReadLineAsync();
                            }
                            else
                            {
                                Log($"[S] [{n}] RCV LINE: {line}");
                            }
                        }
                    }));
                }
            },
            cancellationToken);
    }

    public int Port { get; }

    public string Url => $"127.0.0.1:{Port}";

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _server.Stop();
        foreach (var client in _clients)
        {
            try
            {
                await client.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
            }
            catch (ObjectDisposedException)
            {
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

        try
        {
            await _accept.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
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

    public class Client
    {
        private readonly MockServer _server;
        private readonly TcpClient _tcpClient;

        public Client(MockServer server, TcpClient tcpClient)
        {
            _server = server;
            _tcpClient = tcpClient;
        }

        public void Log(string m) => _server.Log(m);

        public void Close() => _tcpClient.Close();
    }
}
