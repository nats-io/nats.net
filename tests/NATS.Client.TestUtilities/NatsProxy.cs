using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace NATS.Client.Core.Tests;

public class NatsProxy : IDisposable
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly bool _trace;
    private readonly TcpListener _tcpListener;
    private readonly List<TcpClient> _clients = new();
    private readonly List<Frame> _frames = new();
    private readonly Stopwatch _watch = new();
    private int _syncCount;

    public NatsProxy(int port, ITestOutputHelper outputHelper, bool trace)
    {
        _outputHelper = outputHelper;
        _trace = trace;
        _tcpListener = new TcpListener(IPAddress.Loopback, 0);
        _tcpListener.Start();
        _watch.Restart();

        Task.Run(() =>
        {
            var client = 0;
            while (true)
            {
                var tcpClient1 = _tcpListener.AcceptTcpClient();
                TcpClient tcpClient2;
                lock (_clients)
                {
                    tcpClient1.NoDelay = true;
                    tcpClient1.ReceiveBufferSize = 0;
                    tcpClient1.SendBufferSize = 0;
                    _clients.Add(tcpClient1);

                    tcpClient2 = new TcpClient("127.0.0.1", port);
                    tcpClient2.NoDelay = true;
                    tcpClient2.ReceiveBufferSize = 0;
                    tcpClient2.SendBufferSize = 0;
                    _clients.Add(tcpClient2);
                }

                var n = client++;

#pragma warning disable CS4014
                Task.Run(() =>
                {
                    var stream1 = tcpClient1.GetStream();
                    var sr1 = new StreamReader(stream1, Encoding.ASCII);
                    var sw1 = new StreamWriter(stream1, Encoding.ASCII);

                    var stream2 = tcpClient2.GetStream();
                    var sr2 = new StreamReader(stream2, Encoding.ASCII);
                    var sw2 = new StreamWriter(stream2, Encoding.ASCII);

                    Task.Run(() =>
                    {
                        while (NatsProtoDump(n, "C", sr1, sw2, ClientInterceptor))
                        {
                        }
                    });

                    while (NatsProtoDump(n, $"S", sr2, sw1, ServerInterceptor))
                    {
                    }
                });
            }
        });

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                using var tcpClient = new TcpClient();
                tcpClient.Connect(IPAddress.Loopback, Port);
                Log($"Server started on localhost:{Port}");
                return;
            }
            catch (SocketException)
            {
            }
        }

        throw new TimeoutException("Proxy server didn't start");
    }

    public List<Func<string?, string?>> ClientInterceptors { get; } = new();

    public List<Func<string?, string?>> ServerInterceptors { get; } = new();

    public int Port => ((IPEndPoint)_tcpListener.Server.LocalEndPoint!).Port;

    public IReadOnlyList<Frame> AllFrames
    {
        get
        {
            lock (_frames)
            {
                return _frames.ToList();
            }
        }
    }

    public IReadOnlyList<Frame> Frames
    {
        get
        {
            lock (_frames)
            {
                return _frames
                    .Where(f => !Regex.IsMatch(f.Message, @"^(INFO|CONNECT|PING|PONG|\+OK)"))
                    .ToList();
            }
        }
    }

    public IReadOnlyList<Frame> ClientFrames => Frames.Where(f => f.Origin == "C").ToList();

    public IReadOnlyList<Frame> ServerFrames => Frames.Where(f => f.Origin == "S").ToList();

    public void Reset()
    {
        lock (_clients)
        {
            foreach (var tcpClient in _clients)
            {
                try
                {
                    tcpClient.Close();
                }
                catch
                {
                    // ignore
                }
            }

            lock (_frames)
                _frames.Clear();

            _watch.Restart();
        }
    }

    public async Task FlushFramesAsync(NatsConnection nats)
    {
        var subject = $"_SIGNAL_SYNC_{Interlocked.Increment(ref _syncCount)}";

        await nats.PublishAsync(subject);

        await Retry.Until(
            "flush sync frame",
            () => AllFrames.Any(f => f.Message == $"PUB {subject} 0␍␊"));

        lock (_frames)
            _frames.Clear();
    }

    public void Dispose() => _tcpListener.Server.Dispose();

    public string Dump(ReadOnlySpan<char> buffer)
    {
        var sb = new StringBuilder();
        foreach (var c in buffer)
        {
            switch (c)
            {
            case >= ' ' and <= '~':
                sb.Append(c);
                break;
            case '\n':
                sb.Append('␊');
                break;
            case '\r':
                sb.Append('␍');
                break;
            default:
                sb.Append('.');
                break;
            }
        }

        return sb.ToString();
    }

    private string? ClientInterceptor(string? message)
    {
        foreach (var interceptor in ClientInterceptors)
        {
            message = interceptor(message);
        }

        return message;
    }

    private string? ServerInterceptor(string? message)
    {
        foreach (var interceptor in ServerInterceptors)
        {
            message = interceptor(message);
        }

        return message;
    }

    private bool NatsProtoDump(int client, string origin, TextReader sr, TextWriter sw, Func<string?, string?>? interceptor)
    {
        void Write(string? rawFrame)
        {
            if (interceptor != null)
                rawFrame = interceptor(rawFrame);

            if (rawFrame == null)
                return;

            sw.Write(rawFrame);
            sw.Flush();
        }

        string? message;
        try
        {
            message = sr.ReadLine();
        }
        catch
        {
            return false;
        }

        if (message == null)
            return false;

        if (Regex.IsMatch(message, @"^(INFO|CONNECT|PING|PONG|UNSUB|SUB|\+OK|-ERR)"))
        {
            if (client > 0)
                AddFrame(new Frame(_watch.Elapsed, client, origin, message));

            try
            {
                Write($"{message}\r\n");
            }
            catch
            {
                return false;
            }

            return true;
        }

        var match = Regex.Match(message, @"^(?:PUB|HPUB|MSG|HMSG).*?(\d+)\s*$");
        if (match.Success)
        {
            var size = int.Parse(match.Groups[1].Value);
            var buffer = new char[size + 2];
            var span = buffer.AsSpan();
            while (true)
            {
                var read = sr.Read(span);
                if (read == 0)
                    break;
                if (read == -1)
                    return false;
                span = span[read..];
            }

            var bufferDump = Dump(buffer.AsSpan()[..size]);

            try
            {
                Write($"{message}\r\n{new string(buffer)}");
            }
            catch
            {
                return false;
            }

            if (client > 0)
                AddFrame(new Frame(_watch.Elapsed, client, origin, Message: $"{message}␍␊{bufferDump}"));

            return true;
        }

        if (client > 0)
            AddFrame(new Frame(_watch.Elapsed, client, Origin: "ERROR", Message: $"Unknown protocol: {message}"));

        return false;
    }

    private void AddFrame(Frame frame)
    {
        if (_trace)
            Log($"TRACE {frame}");
        lock (_frames)
            _frames.Add(frame);
    }

    private void Log(string text) => _outputHelper.WriteLine($"[PROXY] {DateTime.Now:HH:mm:ss.fff} {text}");

    public record Frame(TimeSpan Timestamp, int Client, string Origin, string Message);
}
