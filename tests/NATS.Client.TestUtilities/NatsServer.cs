using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Diagnostics;

namespace NATS.Client.Core.Tests;

public static class ServerVersions
{
#pragma warning disable SA1310
#pragma warning disable SA1401

    // Changed INFO port reporting for WS connections (nats-server #4255)
    public static Version V2_9_19 = new("2.9.19");

#pragma warning restore SA1401
#pragma warning restore SA1310
}

public class NatsServer : IAsyncDisposable
{
    private static readonly string Ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;
    private static readonly string NatsServerPath = $"nats-server{Ext}";
    private static readonly Version Version;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string? _configFileName;
    private readonly ITestOutputHelper _outputHelper;
    private readonly Task<string[]> _processOut;
    private readonly Task<string[]> _processErr;
    private readonly TransportType _transportType;
    private int _disposed;

    static NatsServer()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = NatsServerPath,
                Arguments = "-v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.Start();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var value = Regex.Match(output, @"v(\d+\.\d+\.\d+)").Groups[1].Value;
        Version = new Version(value);
    }

    public NatsServer()
        : this(new NullOutputHelper(), TransportType.Tcp)
    {
    }

    public NatsServer(ITestOutputHelper outputHelper, TransportType transportType)
        : this(outputHelper, new NatsServerOptionsBuilder().UseTransport(transportType).Build())
    {
    }

    public NatsServer(ITestOutputHelper outputHelper, NatsServerOptions options)
    {
        _outputHelper = outputHelper;
        _transportType = options.TransportType;
        Options = options;
        _configFileName = Path.GetTempFileName();
        var config = options.ConfigFileContents;
        File.WriteAllText(_configFileName, config);
        var cmd = $"{NatsServerPath} -c {_configFileName}";

        outputHelper.WriteLine("ProcessStart: " + cmd + Environment.NewLine + config);
        var (p, stdout, stderr) = ProcessX.GetDualAsyncEnumerable(cmd);

        _processOut = EnumerateWithLogsAsync(stdout, _cancellationTokenSource.Token);
        _processErr = EnumerateWithLogsAsync(stderr, _cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Options.ServerPort, _cancellationTokenSource.Token);
                    if (client.Connected)
                        return;
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(500, _cancellationTokenSource.Token);
            }
        }).Wait(5000); // timeout

        if (_processOut.IsFaulted)
        {
            _processOut.GetAwaiter().GetResult(); // throw exception
        }

        if (_processErr.IsFaulted)
        {
            _processErr.GetAwaiter().GetResult(); // throw exception
        }

        outputHelper.WriteLine("OK to Process Start, Port:" + Options.ServerPort);
    }

    public NatsServerOptions Options { get; }

    public string ClientUrl => _transportType switch
    {
        TransportType.Tcp => $"nats://localhost:{Options.ServerPort}",
        TransportType.Tls => $"tls://localhost:{Options.ServerPort}",
        TransportType.WebSocket => $"ws://localhost:{Options.WebSocketPort}",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public int ConnectionPort
    {
        get
        {
            if (_transportType == TransportType.WebSocket && ServerVersions.V2_9_19 <= Version)
            {
                return Options.WebSocketPort!.Value;
            }
            else
            {
                return Options.ServerPort;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) != 1)
        {
            return;
        }

        _cancellationTokenSource.Cancel(); // trigger of process kill.
        _cancellationTokenSource.Dispose();
        try
        {
            var processLogs = await _processErr; // wait for process exit, nats output info to stderror
            if (processLogs.Length != 0)
            {
                _outputHelper.WriteLine("Process Logs of " + Options.ServerPort);
                foreach (var item in processLogs)
                {
                    _outputHelper.WriteLine(item);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_configFileName != null)
            {
                File.Delete(_configFileName);
            }

            if (Options.ServerDisposeReturnsPorts)
            {
                Options.Dispose();
            }
        }
    }

    public (NatsConnection, NatsProxy) CreateProxiedClientConnection(NatsOptions? options = null)
    {
        if (Options.EnableTls)
        {
            throw new Exception("Tapped mode doesn't work wit TLS");
        }

        var proxy = new NatsProxy(Options.ServerPort, _outputHelper, Options.Trace);

        var client = new NatsConnection((options ?? NatsOptions.Default) with
        {
            LoggerFactory = new OutputHelperLoggerFactory(_outputHelper),
            Url = $"nats://localhost:{proxy.Port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });

        return (client, proxy);
    }

    public NatsConnection CreateClientConnection() => CreateClientConnection(NatsOptions.Default);

    public NatsConnection CreateClientConnection(NatsOptions options)
    {
        return new NatsConnection(ClientOptions(options));
    }

    public NatsConnectionPool CreatePooledClientConnection() => CreatePooledClientConnection(NatsOptions.Default);

    public NatsConnectionPool CreatePooledClientConnection(NatsOptions options)
    {
        return new NatsConnectionPool(4, ClientOptions(options));
    }

    public NatsOptions ClientOptions(NatsOptions options)
    {
        return options with
        {
            LoggerFactory = new OutputHelperLoggerFactory(_outputHelper),

            // ConnectTimeout = TimeSpan.FromSeconds(1),
            // ReconnectWait = TimeSpan.Zero,
            // ReconnectJitter = TimeSpan.Zero,
            TlsOptions = Options.EnableTls
                ? TlsOptions.Default with
                {
                    CertFile = Options.TlsClientCertFile,
                    KeyFile = Options.TlsClientKeyFile,
                    CaFile = Options.TlsCaFile,
                }
                : TlsOptions.Default,
            Url = ClientUrl,
        };
    }

    private async Task<string[]> EnumerateWithLogsAsync(ProcessAsyncEnumerable enumerable, CancellationToken cancellationToken)
    {
        var l = new List<string>();
        try
        {
            await foreach (var item in enumerable.WithCancellation(cancellationToken))
            {
                l.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return l.ToArray();
    }
}

public class NatsCluster : IAsyncDisposable
{
    public NatsCluster(ITestOutputHelper outputHelper, TransportType transportType)
    {
        var opts1 = new NatsServerOptions
        {
            TransportType = transportType,
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var opts2 = new NatsServerOptions
        {
            TransportType = transportType,
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var opts3 = new NatsServerOptions
        {
            TransportType = transportType,
            EnableWebSocket = transportType == TransportType.WebSocket,
            EnableClustering = true,
        };
        var routes = new[] { opts1, opts2, opts3 };
        foreach (var opt in routes)
        {
            opt.SetRoutes(routes);
        }

        Server1 = new NatsServer(outputHelper, opts1);
        Server2 = new NatsServer(outputHelper, opts2);
        Server3 = new NatsServer(outputHelper, opts3);
    }

    public NatsServer Server1 { get; }

    public NatsServer Server2 { get; }

    public NatsServer Server3 { get; }

    public async ValueTask DisposeAsync()
    {
        await Server1.DisposeAsync();
        await Server2.DisposeAsync();
        await Server3.DisposeAsync();
    }
}

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
                        while (NatsProtoDump(n, "C", sr1, sw2))
                        {
                        }
                    });

                    while (NatsProtoDump(n, $"S", sr2, sw1))
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

        throw new TimeoutException("Wiretap server didn't start");
    }

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

    private bool NatsProtoDump(int client, string origin, TextReader sr, TextWriter sw)
    {
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

            sw.WriteLine(message);
            sw.Flush();
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

            var sb = new StringBuilder();
            foreach (var c in buffer.AsSpan()[..size])
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

            sw.WriteLine(message);
            sw.Write(buffer);
            sw.Flush();

            if (client > 0)
                AddFrame(new Frame(_watch.Elapsed, client, origin, Message: $"{message}␍␊{sb}"));

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

public class NullOutputHelper : ITestOutputHelper
{
    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }
}
