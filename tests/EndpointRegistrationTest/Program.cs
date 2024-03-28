using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography.X509Certificates;
using NATS.Client.Core;
using NATS.Client.Services;
using NATS.Client.Services.Controllers;

//using NATS.Client.Services.Controllers;

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
        var controller = new MathController();
        await service.AddEndpointAsync<string>(controller.TestFunc, "TestFunc", "Subject", "QueueGroup");
        
        Console.ReadLine();
    }

    private static ValueTask HandleIncomingMessageAsync(NatsSvcMsg<MyMessage> arg)
    {
        throw new NotImplementedException();
    }
}

internal class MyMessage
{
}

[NatsServiceController(Name = "Name", Version = "Version", QueueGroup = "QueueGroup")]
public class MathController : NatsServiceControllerBase
{
    [NatsServiceEndpoint("divide42", "math-group")]
    public async Task<int> Divide42(int data)
    {
        if (data == 0)
        {
            throw new ArgumentException("Division by zero");
        }

        return 42 / data;
    }

    [NatsServiceEndpoint("getname", "name-group")]
    public async Task<string> GetName(string input) => input + " " + input;

    public ValueTask TestFunc(NatsSvcMsg<string> arg)
    {

        return ValueTask.CompletedTask;
    }
}
