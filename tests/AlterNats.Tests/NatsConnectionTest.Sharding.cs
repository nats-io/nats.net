namespace AlterNats.Tests;

public abstract partial class NatsConnectionTest
{
    // TODO:do.
    [Fact]
    public async Task ConnectionPoolTest()
    {
        await using var server = new NatsServer(output, transportType);

        var conn = server.CreatePooledClientConnection();

        var a = conn.GetConnection();
        var b = conn.GetConnection();
        var c = conn.GetConnection();
        var d = conn.GetConnection();
        var e = conn.GetConnection();

        a.Should().Be(e);
        conn.GetConnections().ToArray().Length.ShouldBe(4);
        new[] { a, b, c, d, e }.Distinct().Count().Should().Be(4);
    }

    [Fact]
    public async Task ShardingConnectionTest()
    {
        await using var server1 = new NatsServer(output, transportType);
        await using var server2 = new NatsServer(output, transportType);
        await using var server3 = new NatsServer(output, transportType);

        var urls = new[] { server1.Ports.ServerPort, server2.Ports.ServerPort, server3.Ports.ServerPort }
            .Select(x => $"nats://localhost:{x}").ToArray();
        var shardedConnection = new NatsShardingConnection(1, NatsOptions.Default, urls);


        var l1 = new List<int>();
        var l2 = new List<int>();
        var l3 = new List<int>();
        await shardedConnection.GetCommand("foo").SubscribeAsync((int x) => l1.Add(x));
        await shardedConnection.GetCommand("bar").SubscribeAsync((int x) => l2.Add(x));
        await shardedConnection.GetCommand("baz").SubscribeAsync((int x) => l3.Add(x));

        await shardedConnection.GetCommand("foo").PublishAsync(10);
        await shardedConnection.GetCommand("bar").PublishAsync(20);
        await shardedConnection.GetCommand("baz").PublishAsync(30);

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        l1.ShouldEqual(10);
        l2.ShouldEqual(20);
        l3.ShouldEqual(30);

        await shardedConnection.GetCommand("foobarbaz").SubscribeRequestAsync((int x) => x * x);

        var r = await shardedConnection.GetCommand("foobarbaz").RequestAsync<int, int>(100);

        r.ShouldBe(10000);
    }
}
