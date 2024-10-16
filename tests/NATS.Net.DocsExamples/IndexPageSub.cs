#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

namespace NATS.Net.DocsExamples;

public class IndexPageSub
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.IndexPageSub");
        {
            #region demo
            await using var nc = new NatsClient("demo.nats.io");

            Console.Write("Enter your room: ");
            var room = Console.ReadLine();

            Console.WriteLine($"Listening for messages on 'hello.{room}.>'");

            await foreach (var msg in nc.SubscribeAsync<string>(subject: $"hello.{room}.>"))
            {
                Console.WriteLine($"Received: {msg.Subject}: {msg.Data}");
            }
            #endregion
        }
    }
}
