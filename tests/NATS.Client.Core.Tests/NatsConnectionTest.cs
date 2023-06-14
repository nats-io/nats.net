using System.Diagnostics;
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
        await using var server = new NatsServer(_output, _transportType);

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();

        var subject = Guid.NewGuid().ToString("N");
        var signalComplete = new WaitSignal();

        var list = new List<int>();
        using var sub = await subConnection.SubscribeAsync<int>(subject);
        sub.Register(x =>
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

        list.ShouldEqual(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public async Task EncodingTest()
    {
        await using var server = new NatsServer(_output, _transportType);

        var serializer1 = NatsOptions.Default.Serializer;

        foreach (var serializer in new INatsSerializer[] { serializer1 })
        {
            var options = NatsOptions.Default with { Serializer = serializer };
            await using var subConnection = server.CreateClientConnection(options);
            await using var pubConnection = server.CreateClientConnection(options);

            var key = Guid.NewGuid().ToString();

            var actual = new List<SampleClass>();
            var signalComplete = new WaitSignal();
            using var d = (await subConnection.SubscribeAsync<SampleClass>(key)).Register(x =>
            {
                actual.Add(x.Data);
                if (x.Data.Id == 30)
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

            actual.ShouldEqual(new[] { one, two, three });
        }
    }

    [Theory]
    [InlineData(10)] // 10 bytes
    [InlineData(32768)] // 32 KiB
    public async Task RequestTest(int minSize)
    {
        await using var server = new NatsServer(_output, _transportType);

        var options = NatsOptions.Default with { RequestTimeout = TimeSpan.FromSeconds(5) };
        await using var subConnection = server.CreateClientConnection(options);
        await using var pubConnection = server.CreateClientConnection(options);

        var subject = Guid.NewGuid().ToString();
        var text = new StringBuilder(minSize).Insert(0, "a", minSize).ToString();

        await using var replyHandle = await subConnection.ReplyAsync<int, string>(subject, x =>
        {
            if (x == 100)
                throw new Exception();
            return text + x;
        });

        await Task.Delay(1000);

        var v = await pubConnection.RequestAsync<int, string>(subject, 9999);
        v.Should().Be(text + 9999);

        // server exception handling
        // TODO: What's the point of server request exceptions?
        // await Assert.ThrowsAsync<NatsException>(async () =>
        // {
        //     await pubConnection.RequestAsync<int, string>(key, 100);
        // });

        // timeout check
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pubConnection.RequestAsync<int, string>("foo", 10);
        });
    }

    [Fact]
    public async Task ReconnectSingleTest()
    {
        using var options = new NatsServerOptions
        {
            EnableWebSocket = _transportType == TransportType.WebSocket,
            ServerDisposeReturnsPorts = false,
        };
        await using var server = new NatsServer(_output, _transportType, options);
        var key = Guid.NewGuid().ToString();

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();
        await subConnection.ConnectAsync(); // wait open
        await pubConnection.ConnectAsync(); // wait open

        var list = new List<int>();
        var waitForReceive300 = new WaitSignal();
        var waitForReceiveFinish = new WaitSignal();
        var d = (await subConnection.SubscribeAsync<int>(key)).Register(x =>
        {
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
        await subConnection.PingAsync(); // wait for subscribe complete

        await pubConnection.PublishAsync(key, 100);
        await pubConnection.PublishAsync(key, 200);
        await pubConnection.PublishAsync(key, 300);

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
        await using var newServer = new NatsServer(_output, _transportType, options);
        await subConnection.ConnectAsync(); // wait open again
        await pubConnection.ConnectAsync(); // wait open again

        _output.WriteLine("RECONNECT COMPLETE, PUBLISH 400 and 500");
        await pubConnection.PublishAsync(key, 400);
        await pubConnection.PublishAsync(key, 500);
        await waitForReceiveFinish;

        list.ShouldEqual(100, 200, 300, 400, 500);
    }

    [Fact(Timeout = 15000)]
    public async Task ReconnectClusterTest()
    {
        await using var cluster = new NatsCluster(_output, _transportType);
        await Task.Delay(TimeSpan.FromSeconds(5)); // wait for cluster completely connected.

        var key = Guid.NewGuid().ToString();

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
        var waitForReceive300 = new WaitSignal();
        var waitForReceiveFinish = new WaitSignal();
        var d = (await connection1.SubscribeAsync<int>(key)).Register(x =>
        {
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
        await connection1.PingAsync(); // wait for subscribe complete

        await connection2.PublishAsync(key, 100);
        await connection2.PublishAsync(key, 200);
        await connection2.PublishAsync(key, 300);
        await waitForReceive300;

        var disconnectSignal = connection1.ConnectionDisconnectedAsAwaitable(); // register disconnect before kill

        _output.WriteLine($"TRY KILL SERVER1 Port:{cluster.Server1.Options.ServerPort}");
        await cluster.Server1.DisposeAsync(); // process kill
        await disconnectSignal;

        await connection1.ConnectAsync(); // wait for reconnect complete.

        connection1.ServerInfo!.Port.Should()
            .BeOneOf(cluster.Server2.Options.ServerPort, cluster.Server3.Options.ServerPort);

        await connection2.PublishAsync(key, 400);
        await connection2.PublishAsync(key, 500);
        await waitForReceiveFinish;
        list.ShouldEqual(100, 200, 300, 400, 500);
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
