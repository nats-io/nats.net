#if NET8_0_OR_GREATER
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities;

namespace NATS.Client.Core2.Tests;

internal enum FakeTransport
{
    /// <summary>Plaintext NATS.</summary>
    Plain,

    /// <summary>INFO in plaintext, then a TLS handshake (the server's default TLS mode).</summary>
    StartTls,

    /// <summary>TLS handshake before INFO (handshake first).</summary>
    TlsFirst,

    /// <summary>
    /// TLS handshake first, then, whatever the outcome, INFO and the rest of the protocol in
    /// plaintext on the raw socket. Lets a test see whether a client that rejected the server
    /// certificate goes on talking plaintext.
    /// </summary>
    TlsFirstThenPlaintext,
}

/// <summary>
/// A scriptable NATS server for tests of what the client does with odd or hostile server input.
/// It answers PING with PONG, records every line it receives and nothing else.
/// </summary>
internal sealed class FakeNatsServer : IAsyncDisposable
{
    private static readonly ConcurrentBag<Socket> HeldPorts = new();

    private readonly TcpListener _listener;
    private readonly TcpListener? _listener6;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, Connection> _connections = new();
    private readonly ITestOutputHelper _output;
    private readonly FakeTransport _transport;
    private readonly X509Certificate2? _cert;
    private readonly string _name;
    private readonly Task _acceptTask;
    private int _count;

    public FakeNatsServer(ITestOutputHelper output, FakeTransport transport = FakeTransport.Plain, X509Certificate2? cert = null, string name = "fake")
    {
        _output = output;
        _transport = transport;
        _cert = cert;
        _name = name;
        (_listener, _listener6) = StartLoopbackListeners();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var listener6 = _listener6;
        _acceptTask = listener6 == null
            ? Task.Run(() => AcceptLoopAsync(_listener))
            : Task.WhenAll(Task.Run(() => AcceptLoopAsync(_listener)), Task.Run(() => AcceptLoopAsync(listener6)));
    }

    public int Port { get; }

    /// <summary>
    /// INFO JSON for the n-th connection (1-based).
    /// </summary>
    public Func<int, string> Info { get; set; } = _ => "{\"server_id\":\"fake\",\"max_payload\":1048576}";

    /// <summary>
    /// Raw protocol text written in plaintext before INFO on the n-th connection (1-based).
    /// </summary>
    public Func<int, string?>? BeforeInfo { get; set; }

    /// <summary>
    /// Awaited on the n-th connection (1-based) before the server side of a TLS handshake, to
    /// keep a client waiting in its handshake.
    /// </summary>
    public Func<int, Task>? BeforeTlsHandshake { get; set; }

    /// <summary>
    /// Raw protocol text written in plaintext right after INFO, in the same write, on the n-th
    /// connection (1-based); with <see cref="FakeTransport.StartTls"/> it precedes the handshake.
    /// </summary>
    public Func<int, string?>? AfterInfo { get; set; }

    /// <summary>
    /// Raw protocol text written right after the PONG that answers the k-th PING (1-based) on
    /// the n-th connection: (n, k) => text. nats-server 2.12+ sends INFO with connect_info
    /// after the first PONG.
    /// </summary>
    public Func<int, int, string?>? AfterPong { get; set; }

    /// <summary>
    /// Whether to close the n-th connection right after answering its k-th PING: (n, k) => close.
    /// </summary>
    public Func<int, int, bool>? CloseAfterPong { get; set; }

    /// <summary>
    /// Every line received, tagged with the 1-based connection number.
    /// </summary>
    public ConcurrentQueue<(int Connection, string Line)> Received { get; } = new();

    public int Connections => Volatile.Read(ref _count);

    /// <summary>
    /// Client debug log to the test output, timestamped like the server's own lines, so a
    /// failure shows both sides of each step.
    /// </summary>
    public static InMemoryTestLoggerFactory ClientLogger(ITestOutputHelper output)
        => new(LogLevel.Debug, m =>
        {
            try
            {
                output.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [client] {m.LogLevel} {m.Message}{(m.Exception == null ? string.Empty : $" ({m.Exception.GetType().Name}: {m.Exception.Message})")}");
            }
            catch (InvalidOperationException)
            {
                // test already finished
            }
        });

    public bool HasReceived(string prefix, int? connection = null)
        => Received.Any(r => r.Line.StartsWith(prefix, StringComparison.Ordinal) && (connection == null || r.Connection == connection));

    /// <summary>
    /// Completes when the client side of the n-th connection is closed.
    /// </summary>
    public Task WaitForClosedAsync(int connection, TimeSpan timeout)
        => Retry.Until($"connection {connection} to {_name} is closed", () => _connections.TryGetValue(connection, out var c) && c.Closed.Task.IsCompleted, timeout: timeout);

    /// <summary>
    /// Writes raw protocol text to every open connection.
    /// </summary>
    public async Task SendAsync(string data)
    {
        foreach (var connection in _connections.Values)
        {
            try
            {
                await connection.WriteAsync(data);
            }
            catch (Exception e)
            {
                Log($"send failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Closes every connection but keeps listening, so the client reconnects here.
    /// </summary>
    public void DisconnectAll()
    {
        foreach (var connection in _connections.Values)
            connection.Tcp.Dispose();
    }

    /// <summary>
    /// Stops listening and closes every connection, so the client has to fail over.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts.IsCancellationRequested)
            return;
        await _cts.CancelAsync();
        _listener.Stop();
        HoldPort(IPAddress.Loopback);
        if (_listener6 != null)
        {
            _listener6.Stop();
            HoldPort(IPAddress.IPv6Loopback);
        }

        foreach (var connection in _connections.Values)
            connection.Tcp.Dispose();
        try
        {
            await _acceptTask;
        }
        catch
        {
            // listener stopped
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // Tests dial "localhost", which resolves to ::1 first on Windows. Nothing listening there
    // makes the connect fail only after the client's connect timeout, so listen on both
    // loopback addresses, on the same port.
    private static (TcpListener V4, TcpListener? V6) StartLoopbackListeners()
    {
        for (var attempt = 0; ; attempt++)
        {
            var v4 = new TcpListener(IPAddress.Loopback, 0);
            v4.Start(16);
            if (!Socket.OSSupportsIPv6)
                return (v4, null);

            var v6 = new TcpListener(IPAddress.IPv6Loopback, ((IPEndPoint)v4.LocalEndpoint).Port);
            try
            {
                v6.Start(16);
                return (v4, v6);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 10)
            {
                // the port is taken on ::1, try another one
                v4.Stop();
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressNotAvailable)
            {
                // no ::1 on this host, so "localhost" can only resolve to 127.0.0.1
                return (v4, null);
            }
        }
    }

    // A client whose server was stopped keeps dialling its port. If the OS handed that port to
    // another test's server, the stray client would show up there as a connection. Keep the
    // port bound, not listening, until the process exits: dials are refused as before and
    // the OS doesn't give the port out again, to this process or any other.
    private void HoldPort(IPAddress address)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(address, Port));
            HeldPorts.Add(socket);
        }
        catch (SocketException e)
        {
            socket.Dispose();
            Log($"port not held on {address}: {e.SocketErrorCode}");
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }

            var n = Interlocked.Increment(ref _count);
            Log($"[{n}] accepted");
            _ = Task.Run(() => ServeAsync(n, tcp));
        }
    }

    private async Task ServeAsync(int n, TcpClient tcp)
    {
        var connection = new Connection(tcp);
        _connections[n] = connection;
        var raw = tcp.GetStream();
        try
        {
            if (_transport is FakeTransport.TlsFirst or FakeTransport.TlsFirstThenPlaintext)
            {
                var ssl = new SslStream(raw, leaveInnerStreamOpen: true);
                try
                {
                    if (BeforeTlsHandshake != null)
                        await BeforeTlsHandshake(n);
                    await ssl.AuthenticateAsServerAsync(_cert!);
                    connection.Stream = ssl;
                    Log($"[{n}] TLS handshake complete");
                }
                catch (Exception e)
                {
                    Log($"[{n}] TLS handshake failed: {e.GetBaseException().Message}");
                    if (_transport == FakeTransport.TlsFirst)
                        return;
                }

                if (_transport == FakeTransport.TlsFirstThenPlaintext)
                {
                    // Give the client time to finish rejecting the certificate.
                    await Task.Delay(200);
                    connection.Stream = raw;
                }
            }

            await connection.WriteAsync($"{BeforeInfo?.Invoke(n)}INFO {Info(n)}\r\n{AfterInfo?.Invoke(n)}");

            if (_transport == FakeTransport.StartTls)
            {
                var ssl = new SslStream(raw, leaveInnerStreamOpen: true);
                if (BeforeTlsHandshake != null)
                    await BeforeTlsHandshake(n);
                await ssl.AuthenticateAsServerAsync(_cert!);
                connection.Stream = ssl;
                Log($"[{n}] TLS handshake complete");
            }

            using var reader = new StreamReader(connection.Stream, Encoding.Latin1);
            while (await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                Log($"[{n}] RCV {line}");
                Received.Enqueue((n, line));
                if (line.StartsWith("PING", StringComparison.Ordinal))
                {
                    var pings = ++connection.Pings;
                    await connection.WriteAsync($"PONG\r\n{AfterPong?.Invoke(n, pings)}");
                    if (CloseAfterPong?.Invoke(n, pings) == true)
                    {
                        Log($"[{n}] closing after PONG {pings}");
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log($"[{n}] {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            Log($"[{n}] closed");
            connection.Closed.TrySetResult();
            tcp.Dispose();
        }
    }

    private void Log(string message)
    {
        try
        {
            _output.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [{_name}:{Port}] {message}");
        }
        catch
        {
            // test already finished
        }
    }

    private sealed class Connection(TcpClient tcp)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public TcpClient Tcp { get; } = tcp;

        public Stream Stream { get; set; } = tcp.GetStream();

        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Pings { get; set; }

        public async Task WriteAsync(string data)
        {
            await _writeLock.WaitAsync();
            try
            {
                await Stream.WriteAsync(Encoding.Latin1.GetBytes(data));
                await Stream.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
#endif
