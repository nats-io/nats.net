using System.Net;
using System.Net.Sockets;
using System.Text;
using NATS.Client.TestUtilities2;

namespace NATS.Client.Core.Tests;

/// <summary>
/// Messages must be delivered as soon as their last byte arrives, regardless of
/// how TCP happens to split the frame.
/// </summary>
public class SplitFrameTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Empty_payload_msg_split_between_cr_and_lf_is_delivered()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var server = new SplittingServer(output);
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, MaxReconnectRetry = 0 });
        await nats.ConnectRetryAsync();

        var sub = await nats.SubscribeCoreAsync<byte[]>("foo", cancellationToken: cts.Token);
        var sid = await server.WaitForSubAsync("foo");

        // MSG foo <sid> 0\r\n\r  then, after the client has certainly read that,  \n
        await server.SendRawAsync($"MSG foo {sid} 0\r\n\r");
        await Task.Delay(500, cts.Token);
        await server.SendRawAsync("\n");

        using var deliveryCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        deliveryCts.CancelAfter(TimeSpan.FromSeconds(5));

        NatsMsg<byte[]> msg;
        try
        {
            msg = await sub.Msgs.ReadAsync(deliveryCts.Token);
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException("message was not delivered after its last byte arrived");
        }

        msg.Subject.Should().Be("foo");
        msg.Data.Should().BeNull();
    }

    /// <summary>
    /// Minimal raw server: INFO, PONG for PING, records SUB sids, then raw writes.
    /// </summary>
    private sealed class SplittingServer : IAsyncDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(60));
        private readonly TaskCompletionSource _accepted = new();
        private readonly Dictionary<string, TaskCompletionSource<string>> _subs = new();
        private readonly Task _serve;
        private TcpClient? _tcpClient;
        private Stream? _stream;

        public SplittingServer(ITestOutputHelper output)
        {
            _output = output;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = Task.Run(ServeAsync);
        }

        public int Port { get; }

        public string Url => $"127.0.0.1:{Port}";

        public async Task SendRawAsync(string data)
        {
            await _accepted.Task.ConfigureAwait(false);
            _output.WriteLine($"[S] SND: {data.Replace("\r", "\r").Replace("\n", "\n")}");
            var bytes = Encoding.ASCII.GetBytes(data);
            await _stream!.WriteAsync(bytes, 0, bytes.Length, _cts.Token);
            await _stream.FlushAsync(_cts.Token);
        }

        public Task<string> WaitForSubAsync(string subject)
        {
            lock (_subs)
            {
            _output.WriteLine($"[S] SND: {data.Replace("\r", "\\r").Replace("\n", "\\n")}");
                {
                    tcs = new TaskCompletionSource<string>();
                    _subs[subject] = tcs;
                }

                return tcs.Task;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            _tcpClient?.Dispose();
            try
            {
                await _serve.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        private async Task ServeAsync()
        {
            _tcpClient = await _listener.AcceptTcpClientAsync();
            _stream = _tcpClient.GetStream();
            var reader = new StreamReader(_stream, Encoding.ASCII);

            var info = Encoding.ASCII.GetBytes("INFO {\"max_payload\":1048576}\r\n");
            await _stream.WriteAsync(info, 0, info.Length, _cts.Token);
            await _stream.FlushAsync(_cts.Token);

            while (!_cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                    break;

                _output.WriteLine($"[S] RCV: {line}");

                if (line.StartsWith("PING"))
                {
                    var pong = Encoding.ASCII.GetBytes("PONG\r\n");
                    await _stream.WriteAsync(pong, 0, pong.Length, _cts.Token);
                    await _stream.FlushAsync(_cts.Token);
                    _accepted.TrySetResult();
                }
                else if (line.StartsWith("SUB"))
                {
                    // SUB <subject> [queue] <sid>
                    var parts = line.Split(' ');
                    var subject = parts[1];
                    var sid = parts[parts.Length - 1];
                    lock (_subs)
                    {
                        if (_subs.TryGetValue(subject, out var tcs))
                        {
                            tcs.TrySetResult(sid);
                        }
                        else
                        {
                            var done = new TaskCompletionSource<string>();
                            done.SetResult(sid);
                            _subs[subject] = done;
                        }
                    }
                }
            }
        }
    }
}
