using JetBrains.dotMemoryUnit;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;

namespace NATS.Client.Core.MemoryTests;

public class NatsConsumeTests
{
    [Test]
    public void Subscription_should_not_be_collected_when_in_consume_async_enumerator()
    {
        var server = NatsServer.StartJS();
        try
        {
            var nats = server.CreateClientConnection();
            var js = new NatsJSContext(nats);

            var sync = new TaskCompletionSource();

            var sub = Task.Run(async () =>
            {
                await js.CreateStreamAsync("s1", new[] { "s1.*" });

                var consumer = await js.CreateConsumerAsync("s1", "c1");

                var count = 0;
                await foreach (var msg in consumer.ConsumeAsync<int>(opts: new NatsJSConsumeOpts { MaxMsgs = 100 }))
                {
                    if (msg.Data == -1)
                    {
                        sync.SetResult();
                        continue;
                    }

                    if (++count == 20)
                        break;
                }
            });

            var pub = Task.Run(async () =>
            {
                var ack1 = await js.PublishAsync("s1.x", -1);
                ack1.EnsureSuccess();
                await sync.Task;

                for (var i = 0; i < 20; i++)
                {
                    GC.Collect();

                    dotMemory.Check(memory =>
                    {
                        var count = memory.GetObjects(where => where.Type.Is<NatsJSConsume<int>>()).ObjectsCount;
                        Assert.That(count, Is.EqualTo(1), $"Alive {i}");
                    });

                    var ack = await js.PublishAsync("s1.x", i);
                    ack.EnsureSuccess();
                }
            });

            var waitPub = Task.WaitAll(new[] { pub }, TimeSpan.FromSeconds(30));
            if (!waitPub)
            {
                Assert.Fail("Timed out waiting for pub task to complete");
            }

            var waitSub = Task.WaitAll(new[] { sub }, TimeSpan.FromSeconds(30));
            if (!waitSub)
            {
                Assert.Fail("Timed out waiting for sub task to complete");
            }

            GC.Collect();

            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsJSConsume<int>>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(0), "Collected");
            });
        }
        finally
        {
            server.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_when_in_ordered_consume_async_enumerator()
    {
        var server = NatsServer.StartJS();
        try
        {
            var nats = server.CreateClientConnection();
            var js = new NatsJSContext(nats);

            var sync = new TaskCompletionSource();

            var sub = Task.Run(async () =>
            {
                await js.CreateStreamAsync("s1", new[] { "s1.*" });

                var consumer = await js.CreateOrderedConsumerAsync("s1");

                var count = 0;
                await foreach (var msg in consumer.ConsumeAsync<int>(opts: new NatsJSConsumeOpts { MaxMsgs = 100 }))
                {
                    if (msg.Data == -1)
                    {
                        sync.SetResult();
                        continue;
                    }

                    if (++count == 20)
                        break;
                }
            });

            var pub = Task.Run(async () =>
            {
                var ack1 = await js.PublishAsync("s1.x", -1);
                ack1.EnsureSuccess();
                await sync.Task;

                for (var i = 0; i < 20; i++)
                {
                    GC.Collect();

                    dotMemory.Check(memory =>
                    {
                        var count = memory.GetObjects(where => where.Type.Is<NatsJSOrderedConsume<int>>()).ObjectsCount;
                        Assert.That(count, Is.EqualTo(1), $"Alive {i}");
                    });

                    var ack = await js.PublishAsync("s1.x", i);
                    ack.EnsureSuccess();
                }
            });

            var waitPub = Task.WaitAll(new[] { pub }, TimeSpan.FromSeconds(30));
            if (!waitPub)
            {
                Assert.Fail("Timed out waiting for pub task to complete");
            }

            var waitSub = Task.WaitAll(new[] { sub }, TimeSpan.FromSeconds(30));
            if (!waitSub)
            {
                Assert.Fail("Timed out waiting for sub task to complete");
            }

            GC.Collect();

            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsJSOrderedConsume<int>>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(0), "Collected");
            });
        }
        finally
        {
            server.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
