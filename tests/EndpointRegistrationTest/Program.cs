using NATS.Client.Core;
using NATS.Client.Services;

namespace EndpointRegistrationTest;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");


        await using var nats = new NatsConnection();
        var svc = new NatsSvcContext(nats);
        await using var testService = await svc.AddServiceAsync("test", "1.0.0");

        var registrar = INatsSvcEndpointRegistrar.GetRegistrar();
        await registrar.RegisterEndpointsAsync(testService);
        
        Console.ReadLine();
    }
}

public class MyClass
{
    [ServiceEndpoint("divide42", "math-group")]
    public async Task<int> Divide42(int data)
    {
        if (data == 0)
        {
            throw new ArgumentException("Division by zero");
        }

        return 42 / data;
    }

    [ServiceEndpoint("getname", "name-group")]
    public async Task<string> GetName(string input) => input + " " + input;
}
