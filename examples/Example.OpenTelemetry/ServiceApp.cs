using System.Diagnostics;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Example.OpenTelemetry;

public static class ServiceApp
{
    public static async Task Run()
    {
        var serviceName = "ServiceApp";
        var serviceVersion = "1.0.0";

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .AddSource("NATS.Net")
            .AddSource("MyServiceSource")
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
