using System.Text;
using MessagePack;

namespace AlterNats.Tests;

public abstract partial class NatsConnectionTest
{
    readonly ITestOutputHelper output;
    readonly TransportType transportType;

    protected NatsConnectionTest(ITestOutputHelper output, TransportType transportType)
    {
        this.output = output;
        this.transportType = transportType;
    }

    [Fact]
    public async Task SimplePubSubTest()
    {
        await using var server = new NatsServer(output, transportType);

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();

        var key = new NatsKey(Guid.NewGuid().ToString("N"));
        var signalComplete = new WaitSignal();

        var list = new List<int>();
        await subConnection.SubscribeAsync<int>(key, x =>
        {
            output.WriteLine($"Received: {x}");
            list.Add(x);
            if (x == 9)
            {
                signalComplete.Pulse();
            }
        });
        await subConnection.PingAsync(); // wait for subscribe complete

        for (int i = 0; i < 10; i++)
        {
            await pubConnection.PublishAsync(key, i);
        }

        await signalComplete;

        list.ShouldEqual(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public async Task EncodingTest()
    {
        await using var server = new NatsServer(output, transportType);

        var serializer1 = NatsOptions.Default.Serializer;
        var serializer2 = new MessagePackNatsSerializer();

        foreach (var serializer in new INatsSerializer[] { serializer1, serializer2 })
        {
            var options = NatsOptions.Default with { Serializer = serializer };
            await using var subConnection = server.CreateClientConnection(options);
            await using var pubConnection = server.CreateClientConnection(options);

            var key = Guid.NewGuid().ToString();

            var actual = new List<SampleClass>();
            var signalComplete = new WaitSignal();
            using var d = await subConnection.SubscribeAsync<SampleClass>(key, x =>
            {
                actual.Add(x);
                if (x.Id == 30) signalComplete.Pulse();
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
        await using var server = new NatsServer(output, transportType);

        var options = NatsOptions.Default with { RequestTimeout = TimeSpan.FromSeconds(5) };
        await using var subConnection = server.CreateClientConnection(options);
        await using var pubConnection = server.CreateClientConnection(options);


        var key = Guid.NewGuid().ToString();
        var text = new StringBuilder(minSize).Insert(0, "a", minSize).ToString();

        await subConnection.SubscribeRequestAsync<int, string>(key, x =>
        {
            if (x == 100) throw new Exception();
            return text + x;
        });

        await Task.Delay(1000);

        var v = await pubConnection.RequestAsync<int, string>(key, 9999);
        v.Should().Be(text + 9999);

        // server exception handling
        await Assert.ThrowsAsync<NatsException>(async () =>
        {
            await pubConnection.RequestAsync<int, string>(key, 100);
        });

        // timeout check
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pubConnection.RequestAsync<int, string>("foo", 10);
        });
    }

    [Fact]
    public async Task ReconnectSingleTest()
    {
        using var ports = new NatsServerPorts(new NatsServerPortOptions
        {
            WebSocket = transportType == TransportType.WebSocket,
            ServerDisposeReturnsPorts = false
        });
        await using var server = new NatsServer(output, transportType, ports);
        var key = Guid.NewGuid().ToString();

        await using var subConnection = server.CreateClientConnection();
        await using var pubConnection = server.CreateClientConnection();
        await subConnection.ConnectAsync(); // wait open
        await pubConnection.ConnectAsync(); // wait open

        var list = new List<int>();
        var waitForReceive300 = new WaitSignal();
        var waitForReceiveFinish = new WaitSignal();
        var d = await subConnection.SubscribeAsync(key, (int x) =>
        {
            output.WriteLine("RECEIVED: " + x);
            list.Add(x);
            if (x == 300)
            {
                waitForReceive300.Pulse();
            }

            if (x == 500)
            {
                waitForReceiveFinish.Pulse();
            }
        });
        await subConnection.PingAsync(); // wait for subscribe complete

        await pubConnection.PublishAsync(key, 100);
        await pubConnection.PublishAsync(key, 200);
        await pubConnection.PublishAsync(key, 300);

        output.WriteLine("TRY WAIT RECEIVE 300");
        await waitForReceive300;

        var disconnectSignal1 = subConnection.ConnectionDisconnectedAsAwaitable();
        var disconnectSignal2 = pubConnection.ConnectionDisconnectedAsAwaitable();

        output.WriteLine("TRY DISCONNECT START");
        await server.DisposeAsync(); // disconnect server
        await disconnectSignal1;
        await disconnectSignal2;

        // start new nats server on same port
        output.WriteLine("START NEW SERVER");
        await using var newServer = new NatsServer(output, transportType, ports);
        await subConnection.ConnectAsync(); // wait open again
        await pubConnection.ConnectAsync(); // wait open again

        output.WriteLine("RECONNECT COMPLETE, PUBLISH 400 and 500");
        await pubConnection.PublishAsync(key, 400);
        await pubConnection.PublishAsync(key, 500);
        await waitForReceiveFinish;

        list.ShouldEqual(100, 200, 300, 400, 500);
    }

    [Fact(Timeout = 15000)]
    public async Task ReconnectClusterTest()
    {
        await using var cluster = new NatsCluster(output, transportType);
        await Task.Delay(TimeSpan.FromSeconds(5)); // wait for cluster completely connected.

        var key = Guid.NewGuid().ToString();

        await using var connection1 = cluster.Server1.CreateClientConnection();
        await using var connection2 = cluster.Server2.CreateClientConnection();
        await using var connection3 = cluster.Server3.CreateClientConnection();

        await connection1.ConnectAsync();
        await connection2.ConnectAsync();
        await connection3.ConnectAsync();

        output.WriteLine("Server1 ClientConnectUrls:" +
                         String.Join(", ", connection1.ServerInfo?.ClientConnectUrls ?? Array.Empty<string>()));
        output.WriteLine("Server2 ClientConnectUrls:" +
                         String.Join(", ", connection2.ServerInfo?.ClientConnectUrls ?? Array.Empty<string>()));
        output.WriteLine("Server3 ClientConnectUrls:" +
                         String.Join(", ", connection3.ServerInfo?.ClientConnectUrls ?? Array.Empty<string>()));

        connection1.ServerInfo!.ClientConnectUrls!.Select(x => new NatsUri(x).Port).Distinct().Count().ShouldBe(3);
        connection2.ServerInfo!.ClientConnectUrls!.Select(x => new NatsUri(x).Port).Distinct().Count().ShouldBe(3);
        connection3.ServerInfo!.ClientConnectUrls!.Select(x => new NatsUri(x).Port).Distinct().Count().ShouldBe(3);

        var list = new List<int>();
        var waitForReceive300 = new WaitSignal();
        var waitForReceiveFinish = new WaitSignal();
        var d = await connection1.SubscribeAsync(key, (int x) =>
        {
            output.WriteLine("RECEIVED: " + x);
            list.Add(x);
            if (x == 300)
            {
                waitForReceive300.Pulse();
            }

            if (x == 500)
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

        output.WriteLine($"TRY KILL SERVER1 Port:{cluster.Server1.Ports.ServerPort}");
        await cluster.Server1.DisposeAsync(); // process kill
        await disconnectSignal;

        await connection1.ConnectAsync(); // wait for reconnect complete.

        connection1.ServerInfo!.Port.Should()
            .BeOneOf(cluster.Server2.Ports.ServerPort, cluster.Server3.Ports.ServerPort);

        await connection2.PublishAsync(key, 400);
        await connection2.PublishAsync(key, 500);
        await waitForReceiveFinish;
        list.ShouldEqual(100, 200, 300, 400, 500);
    }
}

[MessagePackObject]
public class SampleClass : IEquatable<SampleClass>
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string Name { get; set; }

    public SampleClass(int id, string name)
    {
        Id = id;
        Name = name;
    }

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
