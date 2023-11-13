using JetBrains.dotMemoryUnit;
using NATS.Client.Core.Tests;

namespace NATS.Client.Core.MemoryTests;

public class NatsSubTests
{
    [Test]
    public void Subject_manager_should_not_hold_on_to_subscription_if_collected()
    {
        var server = NatsServer.Start();
        try
        {
            var nats = server.CreateClientConnection();

            async Task Isolator()
            {
                // Subscription is not being disposed here
                var natsSub = await nats.SubscribeCoreAsync<string>("foo");
                Assert.That(natsSub.Subject, Is.EqualTo("foo"));
            }

            Isolator().GetAwaiter().GetResult();

            GC.Collect();

            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsSubUtils>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(0));
            });
        }
        finally
        {
            server.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_when_in_async_enumerator()
    {
        var server = NatsServer.Start();
        try
        {
            var nats = server.CreateClientConnection();

            var sync = 0;

            var sub = Task.Run(async () =>
            {
                var count = 0;
                await foreach (var msg in nats.SubscribeAsync<string>("foo.*"))
                {
                    if (msg.Subject == "foo.sync")
                    {
                        Interlocked.Increment(ref sync);
                        continue;
                    }

                    if (++count == 10)
                        break;
                }
            });

            var pub = Task.Run(async () =>
            {
                while (Volatile.Read(ref sync) == 0)
                {
                    await nats.PublishAsync("foo.sync", "sync");
                }

                for (var i = 0; i < 10; i++)
                {
                    GC.Collect();

                    dotMemory.Check(memory =>
                    {
                        var count = memory.GetObjects(where => where.Type.Is<NatsSub<string>>()).ObjectsCount;
                        Assert.That(count, Is.EqualTo(1), "Alive");
                    });

                    await nats.PublishAsync("foo.data", "data");
                }
            });

            var waitPub = Task.WaitAll(new[] { pub }, TimeSpan.FromSeconds(10));
            if (!waitPub)
            {
                Assert.Fail("Timed out waiting for pub task to complete");
            }

            var waitSub = Task.WaitAll(new[] { sub }, TimeSpan.FromSeconds(10));
            if (!waitSub)
            {
                Assert.Fail("Timed out waiting for sub task to complete");
            }

            GC.Collect();

            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsSub<string>>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(0), "Collected");
            });
        }
        finally
        {
            server.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
