
using System.Net.NetworkInformation;
using NATS.Client.Core;
using NATS.Client.Services;
using NATS.Client.Services.Controllers;

namespace EndpointRegistrationTest;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        /*
         NatsSvcConext.AddEndpointControllers(); 
         This will need to create a INatsSvcServer like AddServiceAsync
         Then create a NatsSvcEndpoint<T> for each Controller Endpoint, registering the function as the handler, like AddEndpointAsync does. 
         */

        await using var nats = new NatsConnection();
        var svc = new NatsSvcContext(nats);

        var service = await svc.AddServiceAsync("", "", "q");
        var controller = new PingController();
        await service.AddEndpointAsync<string>(controller.Ping, "TestFunc", "Subject", "QueueGroup");
        
        Console.ReadLine();
    }
}


[NatsServiceController(Name = "Name", Version = "Version", QueueGroup = "QueueGroup")]
public class PingController : NatsServiceControllerBase
{
    [NatsServiceEndpoint("Ping", "Subject", "QueueGroup")]
    public async ValueTask Ping(NatsSvcMsg<string> arg)
    {
        if (arg.Data == "Ping!")
            await arg.ReplyAsync("Pong!");

    }
}
