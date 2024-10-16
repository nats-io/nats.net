// ReSharper disable ArrangeConstructorOrDestructorBody
// ReSharper disable ConvertToPrimaryConstructor
// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable IDE0007
#pragma warning disable IDE0008

using Microsoft.Extensions.Hosting;
using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Core;

public class ReqRepPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Core.ReqRepPage");

        await using var nc1 = new NatsClient();
        var myMathService = new MyMathService(nc1);
        await myMathService.StartAsync(CancellationToken.None);

        await Task.Delay(1000);

        {
            #region reqrep
            await using var nc = new NatsClient();

            NatsMsg<int> reply = await nc.RequestAsync<int, int>("math.double", 2);

            Console.WriteLine($"Received reply: {reply.Data}");
            #endregion
        }

        await myMathService.StopAsync(CancellationToken.None);
    }
}

#region sub
public class MyMathService : BackgroundService
{
    private readonly INatsClient _natsClient;

    public MyMathService(INatsClient natsClient)
    {
        _natsClient = natsClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in _natsClient.SubscribeAsync<int>("math.double", cancellationToken: stoppingToken))
        {
            Console.WriteLine($"Received request: {msg.Data}");

            var result = 2 * msg.Data;

            await msg.ReplyAsync(result, cancellationToken: stoppingToken);
        }
    }
}
#endregion
