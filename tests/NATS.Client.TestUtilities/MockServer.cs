using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NATS.Client.Core.Tests;

#pragma warning disable SA1118

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
#if NET6_0_OR_GREATER
                    var tcpClient = await _server.AcceptTcpClientAsync(cancellationToken);
#else
                    var tcpClient = await _server.AcceptTcpClientAsync();
#endif
                    n++;
                    Log($"[S] [{n}] New client connected");
                    var stream = tcpClient.GetStream();

                    // simple 8-bit encoding so that (int)char == byte
                    var encoding = Encoding.GetEncoding(28591);

                    var sw = new StreamWriter(stream, encoding);
                    await sw.WriteAsync($"INFO {info}\r\n");
                    await sw.FlushAsync();

                    var client = new Client(this, tcpClient, sw);

                    var sr = new StreamReader(stream, encoding);

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

                            Log($"[S] [{n}] RCV {line}");

                            if (line.StartsWith("CONNECT"))
                            {
                                // S: INFO {"option_name":option_value,...}␍␊
                                // C: CONNECT {"option_name":option_value,...}␍␊
                            }
                            else if (line.StartsWith("PING"))
                            {
                                // B: PING␍␊
                                // B: PONG␍␊
                                await sw.WriteAsync("PONG\r\n");
                                await sw.FlushAsync();
                            }
                            else if (line.StartsWith("SUB"))
                            {
                                // C: SUB <subject> [queue group] <sid>␍␊
                                // C: UNSUB <sid> [max_msgs]␍␊
                                var m = Regex.Match(line, @"^SUB\s+(?<subject>\S+)(?:\s+(?<queueGroup>\S+))?\s+(?<sid>\S+)$");
                                var subject = m.Groups["subject"].Value;
                                var sid = m.Groups["sid"].Value;
                                await handler(client, new Cmd("SUB", subject, null, 0, 0, null, sid));
                            }
                            else if (line.StartsWith("PUB") || line.StartsWith("HPUB"))
                            {
                                // C: PUB <subject> [reply-to] <#bytes>␍␊[payload]␍␊
                                // C: HPUB <subject> [reply-to] <#header-bytes> <#total-bytes>␍␊[headers]␍␊␍␊[payload]␍␊
                                Match m;
                                m = Regex.Match(
                                    input: line,
                                    pattern: """
                                             ^(H?PUB)
                                              \s+(?<subject>\S+)
                                              (?:\s+(?<replyTo>\S+))?
                                              (?:\s+(?<hsize>\d+))?
                                              \s+(?<size>\d+)$
                                             """,
                                    RegexOptions.IgnorePatternWhitespace);
                                var subject = m.Groups["subject"].Value;
                                var replyTo = m.Groups["replyTo"].Value;
                                var size = int.Parse(m.Groups["size"].Value);
                                var hsizeValue = m.Groups["hsize"].Value;
                                var hsize = int.Parse(string.IsNullOrWhiteSpace(hsizeValue) ? "0" : hsizeValue);
                                var read = 0;
                                var buffer = new char[size];
                                while (read < size)
                                {
                                    var received = await sr.ReadAsync(buffer, read, size - read);
                                    read += received;
                                }

                                // Log($"[S] RCV PUB payload: {new string(buffer)}");
                                await sr.ReadLineAsync();

                                await handler(client, new Cmd("PUB", subject, replyTo, size, hsize, buffer, string.Empty));
                            }
                            else
                            {
                                // S: MSG <subject> <sid> [reply-to] <#bytes>␍␊[payload]␍␊
                                // S: HMSG <subject> <sid> [reply-to] <#header-bytes> <#total-bytes>␍␊[headers]␍␊␍␊[payload]␍␊
                                Log($"[S] [{n}] RCV LINE NOT PROCESSED: {line}");
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
        catch (ObjectDisposedException)
        {
        }
    }

    public void Log(string m) => _logger(m);

    public record Cmd(string Name, string Subject, string? ReplyTo, int Size, int Hsize, char[]? Buffer, string Sid);

    public class Client
    {
        private readonly MockServer _server;
        private readonly TcpClient _tcpClient;

        public Client(MockServer server, TcpClient tcpClient, StreamWriter writer)
        {
            Writer = writer;
            _server = server;
            _tcpClient = tcpClient;
        }

        public StreamWriter Writer { get; }

        public void Log(string m) => _server.Log(m);

        public void Close() => _tcpClient.Close();
    }
}
