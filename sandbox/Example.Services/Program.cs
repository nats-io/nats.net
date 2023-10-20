using System.Text;
using NATS.Client.Core;
using NATS.Client.Services;

var nats = new NatsConnection();
var svc = new NatsSvcContext(nats);

var qg = args.Length > 0 ? args[0] : "q";

await using var testService = await svc.AddServiceAsync("test", "1.0.0", qg);

await testService.AddEndPointAsync("bla", async m =>
{
    string message;
    using (m.Data)
    {
        message = Encoding.ASCII.GetString(m.Data.Span);
    }

    Console.WriteLine($"[MSG] {m.Subject}: {message}");

    await Task.Delay(Random.Shared.Next(10, 100));
    await m.ReplyAsync(42);
});

Console.ReadLine();
