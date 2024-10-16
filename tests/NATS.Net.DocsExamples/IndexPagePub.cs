#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

namespace NATS.Net.DocsExamples;

public class IndexPagePub
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.IndexPagePub");
        {
            #region demo
            await using var nc = new NatsClient("demo.nats.io");

            Console.Write("Enter your room: ");
            var room = Console.ReadLine();

            Console.Write("Enter your name: ");
            var name = Console.ReadLine();

            while (true)
            {
                Console.Write("Enter a message to publish: ");
                var message = Console.ReadLine();
                await nc.PublishAsync(subject: $"hello.{room}.{name}", data: message);
            }
            #endregion
        }
    }
}
