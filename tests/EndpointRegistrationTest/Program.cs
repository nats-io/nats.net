using NATS.Client.Core;
using NATS.Client.Services;
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
        //await svc.AddEndpointControllers();


        //await using var testService = await svc.AddServiceAsync("test", "1.0.0");


        //IDictionary<string, string>? metadata = null;
        //INatsDeserialize<MyMessage>? serializer = null;
        //CancellationToken cancellationToken = default;

        //await testService.AddEndpointAsync<MyMessage>(
        //    HandleIncomingMessageAsync,
        //    name: "MyEndpoint",
        //    subject: "my.subject",
        //    queueGroup: "MyQueueGroup",
        //    metadata: metadata,
        //    serializer: serializer,
        //    cancellationToken: cancellationToken
        //);

        //var registrar = INatsSvcEndpointRegistrar.GetRegistrar();
        //await registrar.RegisterEndpointsAsync(testService);
        
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

//[NatsServiceController]
//public class MathController : NatsServiceControllerBase
//{
//    [NatsServiceEndpoint("divide42", "math-group")]
//    public async Task<int> Divide42(int data)
//    {
//        if (data == 0)
//        {
//            throw new ArgumentException("Division by zero");
//        }

//        return 42 / data;
//    }

//    [NatsServiceEndpoint("getname", "name-group")]
//    public async Task<string> GetName(string input) => input + " " + input;
//}
