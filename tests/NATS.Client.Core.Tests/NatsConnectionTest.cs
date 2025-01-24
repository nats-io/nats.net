using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

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
        await using var server = await NatsServer.StartAsync(_output, _transportType);

        await using var subConnection = await server.CreateClientConnectionAsync();
        await using var pubConnection = await server.CreateClientConnectionAsync();

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
        await using var server = await NatsServer.StartWithTraceAsync(_output);

        // For no_responders to work we need to the publisher and subscriber to be using the same connection
        await using var subConnection = await server.CreateClientConnectionAsync();

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
        await using var server = await NatsServer.StartAsync(_output, _transportType);

        var serializer1 = new NatsJsonContextSerializerRegistry(SimpleClassJsonSerializerContext.Default);

        foreach (var serializer in new INatsSerializerRegistry[] { serializer1 })
        {
            var options = NatsOpts.Default with { SerializerRegistry = serializer };
            await using var subConnection = await server.CreateClientConnectionAsync(options);
            await using var pubConnection = await server.CreateClientConnectionAsync(options);

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
        await using var server = await NatsServer.StartAsync(_output, _transportType);

        var options = NatsOpts.Default with { RequestTimeout = TimeSpan.FromSeconds(5) };
        await using var subConnection = await server.CreateClientConnectionAsync(options);
        await using var pubConnection = await server.CreateClientConnectionAsync(options);

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

        await using var server = await NatsServer.StartAsync(_output, options);
        var subject = Guid.NewGuid().ToString();

        await using var subConnection = await server.CreateClientConnectionAsync();
        await using var pubConnection = await server.CreateClientConnectionAsync();
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
        await using var newServer = await NatsServer.StartAsync(_output, options);
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
        await cluster.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(5)); // wait for cluster completely connected.

        var subject = Guid.NewGuid().ToString();

        await using var connection1 = await cluster.Server1.CreateClientConnectionAsync();
        await using var connection2 = await cluster.Server2.CreateClientConnectionAsync();
        await using var connection3 = await cluster.Server3.CreateClientConnectionAsync();

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

    [Fact]
    public void NewInboxEmptyPrefixReturnsNuid()
    {
        var opts = NatsOpts.Default with { InboxPrefix = string.Empty };
        var conn = new NatsConnection(opts);

        var inbox = conn.NewInbox();

        Assert.Matches("[A-z0-9]{22}", inbox);
    }

    [Fact]
    public void NewInboxNonEmptyPrefixReturnsPrefixWithNuid()
    {
        var opts = NatsOpts.Default with { InboxPrefix = "PREFIX" };
        var conn = new NatsConnection(opts);

        var inbox = conn.NewInbox();

        Assert.Matches("PREFIX\\.[A-z0-9]{22}", inbox);
    }

    [Fact]
    public void NewInboxVeryLongPrefixReturnsPrefixWithNuid()
    {
        var opts = NatsOpts.Default with { InboxPrefix = new string('A', 512) };
        var conn = new NatsConnection(opts);

        var inbox = conn.NewInbox();

        Assert.Matches("A{512}\\.[A-z0-9]{22}", inbox);
    }

    [Fact]
    public async Task OnSocketAvailableAsync_ShouldBeInvokedOnInitialConnection()
    {
        // Arrange
        await using var server = await NatsServer.StartAsync();
        var clientOpts = server.ClientOpts(NatsOpts.Default);

        var wasInvoked = false;
        var nats = new NatsConnection(clientOpts);
        nats.OnSocketAvailableAsync = async socket =>
        {
            wasInvoked = true;
            await Task.Delay(10);
            return socket;
        };

        // Act
        await nats.ConnectAsync();

        // Assert
        Assert.True(wasInvoked, "OnSocketAvailableAsync should be invoked on initial connection.");
    }

    [Fact]
    public async Task OnSocketAvailableAsync_ShouldBeInvokedOnReconnection()
    {
        // Arrange
        await using var server = await NatsServer.StartAsync();
        var clientOpts = server.ClientOpts(NatsOpts.Default);

        var invocationCount = 0;
        var nats = new NatsConnection(clientOpts);
        nats.OnSocketAvailableAsync = async socket =>
        {
            invocationCount++;
            await Task.Delay(10);
            return socket;
        };

        // Simulate initial connection
        await nats.ConnectAsync();

        // Simulate disconnection
        await server.StopAsync();

        // Act
        // Simulate reconnection
        await server.StartServerProcessAsync();
        await nats.ConnectAsync();

        // Assert
        Assert.Equal(2, invocationCount);
    }

    [Fact]
    public async Task ReconnectOnOpenConnection_ShouldDisconnectAndOpenNewConnection()
    {
        // Arrange
        await using var server = await NatsServer.StartAsync(_output, _transportType);
        await using var connection = await server.CreateClientConnectionAsync();
        await connection.ConnectAsync(); // wait first connection open

        var openedCount = 0;
        var disconnectedCount = 0;

        var openSignal = new WaitSignal();
        var disconnectSignal = new WaitSignal();

        connection.ConnectionOpened += (_, _) =>
        {
            Interlocked.Increment(ref openedCount);
            openSignal.Pulse();
            return default;
        };
        connection.ConnectionDisconnected += (_, _) =>
        {
            Interlocked.Increment(ref disconnectedCount);
            disconnectSignal.Pulse();
            return default;
        };

        // Act
        await connection.ReconnectAsync();
        await disconnectSignal;
        await openSignal;

        // Assert
        // First connection is not taken into account, so one invocation of
        // disconnected event and open connection event are expected
        openedCount.ShouldBe(1);
        disconnectedCount.ShouldBe(1);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task LameDuckModeActivated_EventHandlerShouldBeInvokedWhenInfoWithLDMReceived()
    {
        await using var natsServer = await NatsServer.StartAsync(
            _output,
            new NatsServerOptsBuilder()
                .AddServerConfigText("""
                                     accounts: {
                                       SYS: {
                                         users: [
                                           {user: "sys", password: "password"}
                                         ]
                                       },
                                     }

                                     system_account: SYS
                                     """)
                .UseTransport(_transportType)
                .Build());

        var natsOpts = new NatsOpts
        {
            Url = natsServer.ClientUrl,
            AuthOpts = new NatsAuthOpts
            {
                Username = "sys",
                Password = "password",
            },
        };

        await using var connection = await natsServer.CreateClientConnectionAsync(natsOpts);
        await connection.ConnectAsync();

        var invocationCount = 0;
        var ldmSignal = new WaitSignal();

        connection.LameDuckModeActivated += (_, _) =>
        {
            Interlocked.Increment(ref invocationCount);
            ldmSignal.Pulse();
            return default;
        };

        var subject = $"$SYS.REQ.SERVER.{connection.ServerInfo!.Id}.LDM";

        // Act
        await connection.RequestAsync<string, string>(
            subject: subject,
            data: $$"""{"cid":{{connection.ServerInfo!.ClientId}}}""");
        await ldmSignal;

        // Assert
        invocationCount.ShouldBe(1);
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
