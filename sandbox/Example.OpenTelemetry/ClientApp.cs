using System.Diagnostics;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Example.OpenTelemetry;

public static class ClientApp
{
    public static async Task Run()
    {
        var serviceName = "ClientApp";
        var serviceVersion = "1.0.0";

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .AddSource("NATS.Net")
            .AddSource("MyClientSource")
            .Build();

        ActivitySource activitySource = new("MyClientSource");

        Console.WriteLine("Client App is starting...");

        await using var nats = new NatsConnection();

        using (var activity = activitySource.StartActivity("SayHi"))
        {
            await nats.PublishAsync("greet.presence.client.app", "ClientApp is here!");

            var response = await nats.RequestAsync<string, string>("greet.hi", "Hi, telemetry!");
            Console.WriteLine($"Response: {response}");
        }
    }
}
