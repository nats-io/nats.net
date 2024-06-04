using System.Threading.Channels;
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
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });

            async Task Isolator()
            {
                // Subscription is not being disposed here
                var natsSub = await nats.SubscribeCoreAsync<string>("foo");
                Assert.That(natsSub.Subject, Is.EqualTo("foo"));
                dotMemory.Check(memory =>
                {
                    var count = memory.GetObjects(where => where.Type.Is<NatsSub<string>>()).ObjectsCount;
                    Assert.That(count, Is.EqualTo(1));
                });
            }

            Isolator().GetAwaiter().GetResult();

            GC.Collect();

            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsSub<string>>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(0));
            });
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_subscribe_async()
    {
        var server = NatsServer.Start();
        try
        {
            const int iterations = 10;
            const string subject = "foo.data";
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
            var received = Channel.CreateUnbounded<object>();

            var subTask = Task.Run(async () =>
            {
                var i = 0;
#pragma warning disable SA1312
                await foreach (var _ in nats.SubscribeAsync<string>(subject))
#pragma warning restore SA1312
                {
                    await received.Writer.WriteAsync(new object());
                    if (++i >= iterations)
                        break;
                }
            });

            RunSubTest(iterations, subject, nats, received, subTask);
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_subscribe_core_async_read_all_async()
    {
        var server = NatsServer.Start();
        try
        {
            const int iterations = 10;
            const string subject = "foo.data";
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
            var received = Channel.CreateUnbounded<object>();

            var subTask = Task.Run(async () =>
            {
                var i = 0;
                await using var sub = await nats.SubscribeCoreAsync<string>(subject);
#pragma warning disable SA1312
                await foreach (var _ in sub.Msgs.ReadAllAsync())
#pragma warning restore SA1312
                {
                    await received.Writer.WriteAsync(new object());
                    if (++i >= iterations)
                        break;
                }
            });

            RunSubTest(iterations, subject, nats, received, subTask);
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_subscribe_core_async_read_async()
    {
        var server = NatsServer.Start();
        try
        {
            const int iterations = 10;
            const string subject = "foo.data";
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
            var received = Channel.CreateUnbounded<object>();

            var subTask = Task.Run(async () =>
            {
                var i = 0;
                await using var sub = await nats.SubscribeCoreAsync<string>(subject);
                while (true)
                {
                    await sub.Msgs.ReadAsync();
                    await received.Writer.WriteAsync(new object());
                    if (++i >= iterations)
                        break;
                }
            });

            RunSubTest(iterations, subject, nats, received, subTask);
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public void Subscription_should_not_be_collected_subscribe_core_async_wait_to_read_async()
    {
        var server = NatsServer.Start();
        try
        {
            const int iterations = 10;
            const string subject = "foo.data";
            var nats = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
            var received = Channel.CreateUnbounded<object>();

            var subTask = Task.Run(async () =>
            {
                var i = 0;
                await using var sub = await nats.SubscribeCoreAsync<string>(subject);
                while (await sub.Msgs.WaitToReadAsync())
                {
                    while (sub.Msgs.TryRead(out _))
                    {
                        await received.Writer.WriteAsync(new object());
                        i++;
                    }

                    if (i >= iterations)
                    {
                        break;
                    }
                }
            });

            RunSubTest(iterations, subject, nats, received, subTask);
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private void RunSubTest(int iterations, string subject, NatsConnection nats, Channel<object> received, Task subTask)
    {
        var i = 0;
        var fail = 0;
        while (true)
        {
            nats.PublishAsync(subject, "data").AsTask().GetAwaiter().GetResult();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                received.Reader.ReadAsync(cts.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                if (++fail <= 10)
                {
                    continue;
                }

                Assert.Fail($"failed to receive a reply 10 times");
            }

            if (++i >= iterations)
                break;

            GC.Collect();
            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsSub<string>>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(1), $"Alive - received {i}");
            });
        }

        subTask.GetAwaiter().GetResult();

        GC.Collect();
        dotMemory.Check(memory =>
        {
            var count = memory.GetObjects(where => where.Type.Is<NatsSub<string>>()).ObjectsCount;
            Assert.That(count, Is.EqualTo(0), "Collected");
        });
    }
}
