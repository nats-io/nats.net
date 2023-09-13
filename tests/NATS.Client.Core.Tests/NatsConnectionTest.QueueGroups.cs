namespace NATS.Client.Core.Tests;

public abstract partial class NatsConnectionTest
{
    [Fact]
    public async Task QueueGroupsTest()
    {
        // Use high enough count to create some distribution among subscribers.
        const int messageCount = 100;

        await using var server = NatsServer.Start(_output, _transportType);

        await using var conn1 = server.CreateClientConnection();
        await using var conn2 = server.CreateClientConnection();
        await using var conn3 = server.CreateClientConnection();

        var sub1 = await conn1.SubscribeAsync<int>("foo.*", queueGroup: "my-group");
        var sub2 = await conn2.SubscribeAsync<int>("foo.*", queueGroup: "my-group");

        var signal = new WaitSignal();
        var cts = new CancellationTokenSource();
        cts.Token.Register(() => signal.Pulse());
        var count = 0;

        var sync1 = 0;
        var messages1 = new List<int>();
        var reader1 = Task.Run(
            async () =>
            {
                await foreach (var msg in sub1.Msgs.ReadAllAsync(cts.Token))
                {
                    if (msg.Subject == "foo.sync")
                    {
                        Interlocked.Exchange(ref sync1, 1);
                        continue;
                    }

                    Assert.Equal($"foo.xyz{msg.Data}", msg.Subject);
                    lock (messages1)
                        messages1.Add(msg.Data);
                    var total = Interlocked.Increment(ref count);
                    if (total == messageCount)
                        cts.Cancel();
                }
            },
            cts.Token);

        var sync2 = 0;
        var messages2 = new List<int>();
        var reader2 = Task.Run(
            async () =>
            {
                await foreach (var msg in sub2.Msgs.ReadAllAsync(cts.Token))
                {
                    if (msg.Subject == "foo.sync")
                    {
                        Interlocked.Exchange(ref sync2, 1);
                        continue;
                    }

                    Assert.Equal($"foo.xyz{msg.Data}", msg.Subject);
                    lock (messages2)
                        messages2.Add(msg.Data);
                    var total = Interlocked.Increment(ref count);
                    if (total == messageCount)
                        cts.Cancel();
                }
            },
            cts.Token);

        await Retry.Until(
            "subscriptions are active",
            () => Volatile.Read(ref sync1) + Volatile.Read(ref sync2) == 2,
            async () => await conn3.PublishAsync("foo.sync", 0));

        for (var i = 0; i < messageCount; i++)
        {
            await conn3.PublishAsync($"foo.xyz{i}", i);
        }

        try
        {
            await signal;
        }
        catch (TimeoutException)
        {
        }

        var messages = new List<int>();

        // Ensure we have some messages for each subscriber
        lock (messages1)
        {
            Assert.True(messages1.Count >= messageCount / 5, "messages1.Count >= 20%");
            messages.AddRange(messages1);
        }

        lock (messages2)
        {
            Assert.True(messages2.Count >= messageCount / 5, "messages2.Count >= 20%");
            messages.AddRange(messages2);
        }

        // Ensure we received all messages from the two subscribers.
        messages.Sort();
        Assert.Equal(messageCount, messages.Count);
        for (var i = 0; i < messageCount; i++)
        {
            var data = messages[i];
            Assert.Equal(i, data);
        }

        await sub1.DisposeAsync();
        await sub2.DisposeAsync();

        try
        {
            await reader1;
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await reader2;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
