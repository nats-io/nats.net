using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using Xunit.Sdk;

namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    private readonly ITestOutputHelper _output;
    private readonly TransportType _transportType;

    protected NatsConnectionTest(ITestOutputHelper output, TransportType transportType)
    {
        _output = output;
        _transportType = transportType;
    }

    [Fact]
    public async Task SimplePubSubTest()
    {
        await using var server = NatsServer.Start(_output, _transportType);

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();

        var subject = Guid.NewGuid().ToString("N");
        var signalComplete = new WaitSignal();

        var list = new List<int>();
        var sub = await subConnection.SubscribeAsync<int>(subject);
        var register = sub.Register(x =>
        {
            _output.WriteLine($"Received: {x.Data}");
            list.Add(x.Data);
            if (x.Data == 9)
            {
                signalComplete.Pulse();
            }
        });
        await subConnection.PingAsync(); // wait for subscribe complete

        for (var i = 0; i < 10; i++)
        {
            await pubConnection.PublishAsync(subject, i);
        }

        await signalComplete;
        await sub.DisposeAsync();
        await register;

        list.ShouldEqual(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public async Task EncodingTest()
    {
        await using var server = NatsServer.Start(_output, _transportType);

        var serializer1 = NatsOpts.Default.Serializer;

        foreach (var serializer in new INatsSerializer[] { serializer1 })
        {
            var options = NatsOpts.Default with { Serializer = serializer };
            await using var subConnection = server.CreateClientConnection(options);
            await using var pubConnection = server.CreateClientConnection(options);

            var key = Guid.NewGuid().ToString();

            var actual = new List<SampleClass?>();
            var signalComplete = new WaitSignal();
            var sub = await subConnection.SubscribeAsync<SampleClass>(key);
            var register = sub.Register(x =>
            {
                actual.Add(x.Data);
                if (x.Data?.Id == 30)
                    signalComplete.Pulse();
            });
            await subConnection.PingAsync(); // wait for subscribe complete

            var one = new SampleClass(10, "foo");
            var two = new SampleClass(20, "bar");
            var three = new SampleClass(30, "baz");
            await pubConnection.PublishAsync(key, one);
            await pubConnection.PublishAsync(key, two);
            await pubConnection.PublishAsync(key, three);

            await signalComplete;
            await sub.DisposeAsync();
            await register;

            actual.ShouldEqual(new[] { one, two, three });
        }
    }

    [Theory]
    [InlineData(10)] // 10 bytes
    [InlineData(32768)] // 32 KiB
    public async Task RequestTest(int minSize)
    {
        await using var server = NatsServer.Start(_output, _transportType);

        var options = NatsOpts.Default with { RequestTimeout = TimeSpan.FromSeconds(5) };
        await using var subConnection = server.CreateClientConnection(options);
        await using var pubConnection = server.CreateClientConnection(options);

        var subject = Guid.NewGuid().ToString();
        var text = new StringBuilder(minSize).Insert(0, "a", minSize).ToString();

        var sync = 0;
        var sub = await subConnection.SubscribeAsync<int>(subject);
        var reg = sub.Register(async m =>
        {
            if (m.Data < 10)
            {
                Interlocked.Exchange(ref sync, m.Data);
                await m.ReplyAsync("sync");
            }

            if (m.Data == 100)
            {
                await m.ReplyAsync(default(string));
                return;
            }

            await m.ReplyAsync(text + m.Data);
        });

        await Retry.Until(
            "reply handle is ready",
            () => Volatile.Read(ref sync) == 1,
            async () => await pubConnection.PublishAsync(subject, 1, replyTo: "ignore"),
            retryDelay: TimeSpan.FromSeconds(1));

        var v = await pubConnection.RequestAsync<int, string>(subject, 9999);
        v?.Data.Should().Be(text + 9999);

        // server exception handling: respond with the default value of the type.
        var response = await pubConnection.RequestAsync<int, string>(subject, 100);
        Assert.Null(response?.Data);

        // timeout check
        var noReply = await pubConnection.RequestAsync<int, string>("foo", 10, replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) });
        Assert.Null(noReply);

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task ReconnectSingleTest()
    {
        using var options = new NatsServerOpts
        {
            TransportType = _transportType,
            EnableWebSocket = _transportType == TransportType.WebSocket,
            ServerDisposeReturnsPorts = false,
        };
        await using var server = NatsServer.Start(_output, options);
        var subject = Guid.NewGuid().ToString();

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();
        await subConnection.ConnectAsync(); // wait open
        await pubConnection.ConnectAsync(); // wait open

        var list = new List<int>();
        var sync = 0;
        var waitForReceive300 = new WaitSignal();
        var waitForReceiveFinish = new WaitSignal();
        var sub = await subConnection.SubscribeAsync<int>(subject);
        var reg = sub.Register(x =>
        {
            if (x.Data < 10)
            {
                Interlocked.Exchange(ref sync, x.Data);
                return;
            }

            _output.WriteLine("RECEIVED: " + x.Data);
            list.Add(x.Data);
            if (x.Data == 300)
            {
                waitForReceive300.Pulse();
            }

            if (x.Data == 500)
            {
                waitForReceiveFinish.Pulse();
            }
        });

        await Retry.Until(
            "subscription is active (1)",
            () => Volatile.Read(ref sync) == 1,
            async () => await pubConnection.PublishAsync(subject, 1));

        await pubConnection.PublishAsync(subject, 100);
        await pubConnection.PublishAsync(subject, 200);
        await pubConnection.PublishAsync(subject, 300);

        _output.WriteLine("TRY WAIT RECEIVE 300");
        await waitForReceive300;

        var disconnectSignal1 = subConnection.ConnectionDisconnectedAsAwaitable();
        var disconnectSignal2 = pubConnection.ConnectionDisconnectedAsAwaitable();

        _output.WriteLine("TRY DISCONNECT START");
        await server.DisposeAsync(); // disconnect server
        await disconnectSignal1;
        await disconnectSignal2;

        // start new nats server on same port
        _output.WriteLine("START NEW SERVER");
        await using var newServer = NatsServer.Start(_output, options);
        await subConnection.ConnectAsync(); // wait open again
        await pubConnection.ConnectAsync(); // wait open again

        await Retry.Until(
            "subscription is active (2)",
            () => Volatile.Read(ref sync) == 2,
            async () => await pubConnection.PublishAsync(subject, 2));

        _output.WriteLine("RECONNECT COMPLETE, PUBLISH 400 and 500");
        await pubConnection.PublishAsync(subject, 400);
        await pubConnection.PublishAsync(subject, 500);
        await waitForReceiveFinish;

        await sub.DisposeAsync();
        await reg;

        list.ShouldEqual(100, 200, 300, 400, 500);
    }

    [Fact(Timeout = 30000)]
    public async Task ReconnectClusterTest()
    {
        await using var cluster = new NatsCluster(_output, _transportType);
        await Task.Delay(TimeSpan.FromSeconds(5)); // wait for cluster completely connected.

        var subject = Guid.NewGuid().ToString();

        await using var connection1 = cluster.Server1.CreateClientConnection();
        await using var connection2 = cluster.Server2.CreateClientConnection();
        await using var connection3 = cluster.Server3.CreateClientConnection();

        await connection1.ConnectAsync();
        await connection2.ConnectAsync();
        await connection3.ConnectAsync();

        _output.WriteLine("Server1 ClientConnectUrls:" +
                         string.Join(", ", connection1.ServerInfo?.ClientConnectUrls ?? Array.Empty<string>()));
        _output.WriteLine("Server2 ClientConnectUrls:" +
                         string.Join(", ", connection2.ServerInfo?.ClientConnectUrls ?? Array.Empty<string>()));
        _output.WriteLine("Server3 ClientConnectUrls:" +
                         string.Join(", ", connection3.ServerInfo?.ClientConnectUrls ?? Array.Empty<string>()));

        connection1.ServerInfo!.ClientConnectUrls!.Select(x => new NatsUri(x, true).Port).Distinct().Count().ShouldBe(3);
        connection2.ServerInfo!.ClientConnectUrls!.Select(x => new NatsUri(x, true).Port).Distinct().Count().ShouldBe(3);
        connection3.ServerInfo!.ClientConnectUrls!.Select(x => new NatsUri(x, true).Port).Distinct().Count().ShouldBe(3);

        var list = new List<int>();
        var sync = 0;
        var waitForReceive300 = new WaitSignal();
        var waitForReceiveFinish = new WaitSignal();
        var sub = await connection1.SubscribeAsync<int>(subject);
        var reg = sub.Register(x =>
        {
            if (x.Data < 10)
            {
                Interlocked.Exchange(ref sync, x.Data);
                return;
            }

            _output.WriteLine("RECEIVED: " + x.Data);
            list.Add(x.Data);
            if (x.Data == 300)
            {
                waitForReceive300.Pulse();
            }

            if (x.Data == 500)
            {
                waitForReceiveFinish.Pulse();
            }
        });

        await Retry.Until(
            "subscription is active (1)",
            () => Volatile.Read(ref sync) == 1,
            async () => await connection2.PublishAsync(subject, 1),
            retryDelay: TimeSpan.FromSeconds(.5));

        await connection2.PublishAsync(subject, 100);
        await connection2.PublishAsync(subject, 200);
        await connection2.PublishAsync(subject, 300);
        await waitForReceive300;

        var disconnectSignal = connection1.ConnectionDisconnectedAsAwaitable(); // register disconnect before kill

        _output.WriteLine($"TRY KILL SERVER1 Port:{cluster.Server1.Opts.ServerPort}");
        await cluster.Server1.DisposeAsync(); // process kill
        await disconnectSignal;

        Net.WaitForTcpPortToClose(cluster.Server1.ConnectionPort);

        await connection1.ConnectAsync(); // wait for reconnect complete.

        connection1.ServerInfo!.Port.Should().BeOneOf(cluster.Server2.ConnectionPort, cluster.Server3.ConnectionPort);

        await Retry.Until(
            "subscription is active (2)",
            () => Volatile.Read(ref sync) == 2,
            async () => await connection2.PublishAsync(subject, 2),
            retryDelay: TimeSpan.FromSeconds(.5));

        await connection2.PublishAsync(subject, 400);
        await connection2.PublishAsync(subject, 500);
        await waitForReceiveFinish;

        await sub.DisposeAsync();
        await reg;

        list.ShouldEqual(100, 200, 300, 400, 500);
    }

    public static IEnumerable<object[]> TestConfig()
    {
        Type[] types = { typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>) };
        byte[] payload = "hello world"u8.ToArray();
        bool[] waitUntilSend = { true, false };
        INatsSerializer[] natsSerializer = { NatsOpts.Default.Serializer, new ByteSerializer() };

        foreach (var pubType in types)
        {
            foreach (var subType in types)
            {
                foreach (var wait in waitUntilSend)
                {
                    foreach (var serializer in natsSerializer)
                    {
                        yield return new object[] { pubType, subType, wait, serializer, payload };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(TestConfig))]
    public async Task TestBinaryPayload(Type pubType, Type subType, bool waitUntilSend, INatsSerializer serializer, byte[] payload)
    {
        byte[]? received = Array.Empty<byte>();

        if (pubType == typeof(byte[]))
        {
            if (subType == typeof(byte[]))
            {
                received = await TestBinaryPayloadCore<byte[], byte[]>(payload, waitUntilSend, serializer);
            }
            else if (subType == typeof(ReadOnlyMemory<byte>))
            {
                var rec = await TestBinaryPayloadCore<byte[], ReadOnlyMemory<byte>>(payload, waitUntilSend, serializer);
                received = rec.ToArray();
            }
            else if (subType == typeof(Memory<byte>))
            {
                var rec = await TestBinaryPayloadCore<byte[], Memory<byte>>(payload, waitUntilSend, serializer);
                received = rec.ToArray();
            }
        }
        else if (pubType == typeof(Memory<byte>))
        {
            if (subType == typeof(byte[]))
            {
                received = await TestBinaryPayloadCore<Memory<byte>, byte[]>(payload, waitUntilSend, serializer);
            }
            else if (subType == typeof(ReadOnlyMemory<byte>))
            {
                var rec = await TestBinaryPayloadCore<Memory<byte>, ReadOnlyMemory<byte>>(payload, waitUntilSend, serializer);
                received = rec.ToArray();
            }
            else if (subType == typeof(Memory<byte>))
            {
                var rec = await TestBinaryPayloadCore<Memory<byte>, Memory<byte>>(payload, waitUntilSend, serializer);
                received = rec.ToArray();
            }
        }
        else if (pubType == typeof(ReadOnlyMemory<byte>))
        {
            if (subType == typeof(byte[]))
            {
                received = await TestBinaryPayloadCore<ReadOnlyMemory<byte>, byte[]>(payload, waitUntilSend, serializer);
            }
            else if (subType == typeof(ReadOnlyMemory<byte>))
            {
                var rec = await TestBinaryPayloadCore<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(payload, waitUntilSend, serializer);
                received = rec.ToArray();
            }
            else if (subType == typeof(Memory<byte>))
            {
                var rec = await TestBinaryPayloadCore<ReadOnlyMemory<byte>, Memory<byte>>(payload, waitUntilSend, serializer);
                received = rec.ToArray();
            }
        }

        Assert.True(payload.SequenceEqual(received ?? throw new NullReferenceException()));
    }

    public async Task<TSub?> TestBinaryPayloadCore<TPub, TSub>(TPub payload, bool waitUntilSend, INatsSerializer serializer)
    {
        await using var server = NatsServer.Start(_output, _transportType);

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();

        using var timeout = new CancellationTokenSource(5_000);
        var subject = Guid.NewGuid().ToString("N");

        var receivedData = new List<TSub?>();
        var pubOpts = new NatsPubOpts {WaitUntilSent = waitUntilSend, Serializer = serializer };
        var subOpts = new NatsSubOpts { Serializer = serializer };
        await using var sub = await subConnection.SubscribeAsync<TSub>(subject, opts: subOpts, cancellationToken: timeout.Token);

        var receivedTask = Task.Run(
            async () =>
            {
                await sub.Msgs.WaitToReadAsync(timeout.Token);
                sub.Msgs.TryRead(out var msg);
                receivedData.Add(msg.Data);
            },
            timeout.Token);


        await pubConnection.PublishAsync(subject, payload, opts: pubOpts, cancellationToken: timeout.Token);

        await receivedTask;

        var receivedPayload = Assert.Single(receivedData);
        return receivedPayload;
    }

    private sealed class ByteSerializer : INatsSerializer
    {
        public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
        {
            if (value == null)
            {
                return 0;
            }

            if (typeof(T) == typeof(ReadOnlyMemory<byte>))
            {
                var rom = (ReadOnlyMemory<byte>)(object)value;
                var buffer = bufferWriter.GetMemory(rom.Length);
                rom.CopyTo(buffer);
                bufferWriter.Advance(rom.Length);
                return rom.Length;
            }

            if (typeof(T) == typeof(Memory<byte>))
            {
                var mem = (Memory<byte>)(object)value;
                var buffer = bufferWriter.GetMemory(mem.Length);
                mem.CopyTo(buffer);
                bufferWriter.Advance(mem.Length);
                return mem.Length;
            }

            if (typeof(T) == typeof(byte[]))
            {
                var arr = (byte[])(object)value;
                var buffer = bufferWriter.GetMemory(arr.Length);
                arr.CopyTo(buffer);
                bufferWriter.Advance(arr.Length);
                return arr.Length;
            }

            throw new NotSupportedException();
        }

        public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
        {
            var arr = new byte[buffer.Length];
            buffer.CopyTo(arr.AsSpan());

            if (typeof(T) == typeof(ReadOnlyMemory<byte>))
            {
                return (T)(object)new ReadOnlyMemory<byte>(arr);
            }

            if (typeof(T) == typeof(Memory<byte>))
            {
                return (T)(object)new Memory<byte>(arr);
            }

            if (typeof(T) == typeof(byte[]))
            {
                return (T)(object)arr;
            }

            throw new NotSupportedException();
        }

        public object? Deserialize(in ReadOnlySequence<byte> buffer, Type type)
        {
            if (type == typeof(ReadOnlyMemory<byte>))
            {
                return Deserialize<ReadOnlyMemory<byte>>(buffer);
            }
            if (type == typeof(Memory<byte>))
            {
                return Deserialize<Memory<byte>>(buffer);
            }

            if (type == typeof(byte[]))
            {
                return Deserialize<byte[]>(buffer);
            }

            throw new NotSupportedException();
        }
    }



}

public class SampleClass : IEquatable<SampleClass>
{
    public SampleClass(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; set; }

    public string Name { get; set; }

    public bool Equals(SampleClass? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Id == other.Id && Name == other.Name;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SampleClass)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }

    public override string ToString()
    {
        return $"{Id}-{Name}";
    }
}
