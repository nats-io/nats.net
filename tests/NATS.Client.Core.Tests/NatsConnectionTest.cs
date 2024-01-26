using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        var sub = await subConnection.SubscribeCoreAsync<int>(subject);
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
    public async Task PubSubNoRespondersTest()
    {
        await using var server = NatsServer.StartWithTrace(_output);

        // For no_responders to work we need to the publisher and subscriber to be using the same connection
        await using var subConnection = server.CreateClientConnection();

        var signalComplete = new WaitSignal();
        var replyToAddress = subConnection.NewInbox();
        var code = 0;
        var sub = await subConnection.SubscribeCoreAsync<int>(replyToAddress);
        var register = sub.Register(x =>
        {
            if (x.Headers is not null)
            {
                code = x.Headers.Code;
                signalComplete.Pulse();
            }
        });
        await subConnection.PingAsync(); // wait for subscribe complete
        await subConnection.PublishAsync(Guid.NewGuid().ToString(), 1, replyTo: replyToAddress);
        await signalComplete;
        Assert.Equal(503, code);
    }

    [Fact]
    public async Task EncodingTest()
    {
        await using var server = NatsServer.Start(_output, _transportType);

        var serializer1 = new NatsJsonContextSerializerRegistry(SimpleClassJsonSerializerContext.Default);

        foreach (var serializer in new INatsSerializerRegistry[] { serializer1 })
        {
            var options = NatsOpts.Default with { SerializerRegistry = serializer };
            await using var subConnection = server.CreateClientConnection(options);
            await using var pubConnection = server.CreateClientConnection(options);

            var key = Guid.NewGuid().ToString();

            var actual = new List<SampleClass?>();
            var signalComplete = new WaitSignal();
            var sub = await subConnection.SubscribeCoreAsync<SampleClass>(key);
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
        var sub = await subConnection.SubscribeCoreAsync<int>(subject);
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

            // Trigger a timeout
            if (m.Data == 11)
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
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
        v.Data.Should().Be(text + 9999);

        // server exception handling: respond with the default value of the type.
        var response = await pubConnection.RequestAsync<int, string>(subject, 100);
        Assert.Null(response.Data);

        // timeout check
        await Assert.ThrowsAsync<NatsNoReplyException>(async () =>
            await pubConnection.RequestAsync<int, string>(subject, 11, replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(1) }));

        await sub.DisposeAsync();
        await reg;
    }

    [Fact]
    public async Task ReconnectSingleTest()
    {
        var options = new NatsServerOptsBuilder()
            .UseTransport(_transportType)
            .WithServerDisposeReturnsPorts()
            .Build();

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
        var sub = await subConnection.SubscribeCoreAsync<int>(subject);
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
        var sub = await connection1.SubscribeCoreAsync<int>(subject);
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

    [Fact]
    public void InterfaceShouldHaveSamePublicPropertiesEventsAndMethodAsClass()
    {
        var classType = typeof(NatsConnection);
        var interfaceType = typeof(INatsConnection);
        var ignoredMethods = new List<string>
        {
            "GetType",
            "ToString",
            "Equals",
            "GetHashCode",
        };

        var classMethods = classType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !ignoredMethods.Contains(m.Name)).ToList();
        var interfaceMethods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))).ToList();

        foreach (var classInfo in classMethods)
        {
            var name = classInfo.Name;

            interfaceMethods.Select(m => m.Name).Should().Contain(name);
        }
    }
}

[JsonSerializable(typeof(SampleClass))]
public partial class SimpleClassJsonSerializerContext : JsonSerializerContext
{
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
