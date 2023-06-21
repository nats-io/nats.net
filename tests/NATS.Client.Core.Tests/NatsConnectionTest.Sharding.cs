namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    // TODO:do.
    [Fact]
    public async Task ConnectionPoolTest()
    {
        await using var server = new NatsServer(_output, _transportType);

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
        await using var server1 = new NatsServer(_output, _transportType);
        await using var server2 = new NatsServer(_output, _transportType);
        await using var server3 = new NatsServer(_output, _transportType);

        var urls = new[] { server1, server2, server3 }
            .Select(s => s.ClientUrl).ToArray();
        var shardedConnection = new NatsShardingConnection(1, server1.ClientOptions(NatsOptions.Default), urls);

        var l1 = new List<int>();
        var l2 = new List<int>();
        var l3 = new List<int>();
        var sub1 = await shardedConnection.GetCommand("foo").SubscribeAsync<int>();
        var reg1 = sub1.Register(msg => l1.Add(msg.Data));
        var sub2 = await shardedConnection.GetCommand("bar").SubscribeAsync<int>();
        var reg2 = sub2.Register(msg => l2.Add(msg.Data));
        var sub3 = await shardedConnection.GetCommand("baz").SubscribeAsync<int>();
        var reg3 = sub3.Register(msg => l3.Add(msg.Data));

        await shardedConnection.GetCommand("foo").PublishAsync(10);
        await shardedConnection.GetCommand("bar").PublishAsync(20);
        await shardedConnection.GetCommand("baz").PublishAsync(30);

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        l1.ShouldEqual(10);
        l2.ShouldEqual(20);
        l3.ShouldEqual(30);

        await shardedConnection.GetCommand("foobarbaz").ReplyAsync((int x) => x * x);

        var r = await shardedConnection.GetCommand("foobarbaz").RequestAsync<int, int>(100);

        r.ShouldBe(10000);

        await sub1.DisposeAsync();
        await reg1;
        await sub2.DisposeAsync();
        await reg2;
        await sub3.DisposeAsync();
        await reg3;
    }
}
