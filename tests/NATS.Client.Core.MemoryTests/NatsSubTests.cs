using JetBrains.dotMemoryUnit;
using NATS.Client.Core.Tests;

namespace NATS.Client.Core.MemoryTests;

public class NatsSubTests
{
    [Test]
    public void Subject_manager_should_not_hold_on_to_subscription_if_collected()
    {
        var server = new NatsServer();
        try
        {
            var nats = server.CreateClientConnection();

            async Task Isolator()
            {
                // Subscription is not being disposed here
                var natsSub = await nats.SubscribeAsync("foo");
                Assert.That(natsSub.Subject, Is.EqualTo("foo"));
            }

            Isolator().GetAwaiter().GetResult();

            GC.Collect();

            dotMemory.Check(memory =>
            {
                var count = memory.GetObjects(where => where.Type.Is<NatsSub>()).ObjectsCount;
                Assert.That(count, Is.EqualTo(0));
            });
        }
        finally
        {
            server.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
