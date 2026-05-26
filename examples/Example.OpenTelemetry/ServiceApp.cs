using System.Diagnostics;
using NATS.Client.Core;
using NATS.Client.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Example.OpenTelemetry;

public static class ServiceApp
{
    public static async Task Run()
    {
        var serviceName = "ServiceApp";
        var serviceVersion = "1.0.0";

        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName: serviceName, serviceVersion: serviceVersion);

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(resourceBuilder)
            .AddNatsClientInstrumentation()
            .AddSource("MyServiceSource")
            .Build();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(resourceBuilder)
            .AddNatsClientInstrumentation()
            .Build();

        ActivitySource activitySource = new("MyServiceSource");

        Console.WriteLine("Service App is starting...");

        await using var nats = new NatsConnection(new NatsOpts
        {
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        await foreach (var msg in nats.SubscribeAsync<string>("greet.>"))
        {
            using var activity = msg.StartActivity("Greetings");

            if (msg.Subject.StartsWith("greet.presence"))
            {
                Console.WriteLine($"{msg.Data} is here!");

                activity?.AddEvent(new ActivityEvent("Presence", tags: new()
                {
                    ["subject"] = msg.Subject,
                    ["data"] = msg.Data,
                }));

                continue;
            }

            await msg.ReplyAsync($"Hi there! {msg.Data}");
        }
    }
}
