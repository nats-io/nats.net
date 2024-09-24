using JetBrains.dotMemoryUnit;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;
using Xunit.Abstractions;

namespace NATS.Client.Core.MemoryTests;

public class NatsConsumeTests
{
    [Test]
    public void Subscription_should_not_be_collected_when_in_consume_async_enumerator()
    {
        var server = NatsServer.StartJS();
        try
        {
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
            var js = new NatsJSContext(nats);

            nats.PingAsync().AsTask().GetAwaiter().GetResult();

            var sync = new TaskCompletionSource();

            var sub = Task.Run(async () =>
            {
                await js.CreateStreamAsync(new StreamConfig { Name = "s1", Subjects = new[] { "s1.*" } });

                var consumer = await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig { Name = "c1", DurableName = "c1" });

                var count = 0;
                await foreach (var msg in consumer.ConsumeAsync<int>(opts: new NatsJSConsumeOpts { MaxMsgs = 100 }))
                {
                    if (msg.Data == -1)
                    {
                        sync.SetResult();
                        continue;
                    }

                    if (++count == 5)
                        break;
                }
            });

            var pub = Task.Run(async () =>
            {
                for (var j = 0; j < 4; j++)
                {
                    try
                    {
                        var ack1 = await js.PublishAsync("s1.x", -1);
                        ack1.EnsureSuccess();
                        break;
                    }
                    catch (NatsNoRespondersException)
                    {
                        await Task.Delay(100);
                    }
                }

                await sync.Task;

                for (var i = 0; i < 5; i++)
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
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_when_in_ordered_consume_async_enumerator()
    {
        var server = NatsServer.StartJS();
        try
        {
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
            var js = new NatsJSContext(nats);

            nats.PingAsync().AsTask().GetAwaiter().GetResult();

            var sync = new TaskCompletionSource();

            var sub = Task.Run(async () =>
            {
                await js.CreateStreamAsync(new StreamConfig { Name = "s1", Subjects = new[] { "s1.*" } });

                var consumer = await js.CreateOrderedConsumerAsync("s1");

                var count = 0;
                await foreach (var msg in consumer.ConsumeAsync<int>(opts: new NatsJSConsumeOpts { MaxMsgs = 100 }))
                {
                    if (msg.Data == -1)
                    {
                        sync.SetResult();
                        continue;
                    }

                    if (++count == 5)
                        break;
                }
            });

            var pub = Task.Run(async () =>
            {
                for (var j = 0; j < 4; j++)
                {
                    try
                    {
                        var ack1 = await js.PublishAsync("s1.x", -1);
                        ack1.EnsureSuccess();
                        break;
                    }
                    catch (NatsNoRespondersException)
                    {
                        await Task.Delay(100);
                    }
                }

                await sync.Task;

                for (var i = 0; i < 5; i++)
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
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

public class TestTextWriterOutput : ITestOutputHelper
{
    private readonly TextWriter _out;

    public TestTextWriterOutput(TextWriter @out) => _out = @out;

    public void WriteLine(string message) => _out.WriteLine(message);

    public void WriteLine(string format, params object[] args) => _out.WriteLine(format, args);
}
