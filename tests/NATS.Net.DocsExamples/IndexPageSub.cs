using NATS.Client.Core;

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
            await using NatsClient nc = new NatsClient("demo.nats.io");

            Console.Write("Enter your room: ");
            string? room = Console.ReadLine();

            Console.WriteLine($"Listening for messages on 'hello.{room}.>'");

            await foreach (NatsMsg<string> msg in nc.SubscribeAsync<string>(subject: $"hello.{room}.>"))
            {
                Console.WriteLine($"Received: {msg.Subject}: {msg.Data}");
            }
            #endregion
        }
    }
}
