using System.Text;
using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Services;

using var tracer = TracingSetup.RunSandboxTracing();

var opts = NatsOpts.Default with { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()) };

var nats = new NatsConnection(opts);
var svc = new NatsSvcContext(nats);

var qg = args.Length > 0 ? args[0] : "q";

await using var testService = await svc.AddServiceAsync("test", "1.0.0", qg);

await testService.AddEndpointAsync<int>(name: "bla", handler: async m =>
{
    if (m.Exception is { } e)
    {
        Console.WriteLine($"[MSG] Error: {e.GetBaseException().Message}");
        await m.ReplyErrorAsync(999, e.GetBaseException().Message, Encoding.UTF8.GetBytes(e.ToString()));
    }

    Console.WriteLine($"[MSG] {m.Subject}: {m.Data}");

    if (m.Data == 0)
    {
        throw new Exception("Data can't be 0");
    }

    if (m.Data == 1)
    {
        throw new NatsSvcEndpointException(1, "Data can't be 1", "More info ...");
    }

    if (m.Data == 2)
    {
        await m.ReplyErrorAsync(2, "Data can't be 2");
        return;
    }

    await Task.Delay(Random.Shared.Next(10, 100));
    await m.ReplyAsync(42);
});

var grp1 = await testService.AddGroupAsync("grp1");

await grp1.AddEndpointAsync<int>(name: "bla", handler: async m =>
{
    if (m.Exception is { } e)
    {
        Console.WriteLine($"[MSG] Error: {e.GetBaseException().Message}");
        await m.ReplyErrorAsync(999, e.GetBaseException().Message, Encoding.UTF8.GetBytes(e.ToString()));
    }

    Console.WriteLine($"[MSG] {m.Subject}: {m.Data}");

    if (m.Data == 0)
    {
        throw new Exception("Data can't be 0");
    }

    if (m.Data == 1)
    {
        throw new NatsSvcEndpointException(1, "Data can't be 1", "More info ...");
    }

    if (m.Data == 2)
    {
        await m.ReplyErrorAsync(2, "Data can't be 2");
        return;
    }

    await Task.Delay(Random.Shared.Next(10, 100));
    await m.ReplyAsync(42);
});

Console.ReadLine();
