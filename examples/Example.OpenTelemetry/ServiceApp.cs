using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Logs;
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
            .AddSource(NatsTelemetry.SourceName)
            .AddSource("MyServiceSource")
            .Build();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(NatsTelemetry.SourceName)
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.AddOtlpExporter();
            });
        });
        var logger = loggerFactory.CreateLogger(serviceName);

        ActivitySource activitySource = new("MyServiceSource");

        logger.LogInformation("Service App is starting...");

        await using var nats = new NatsConnection(new NatsOpts
        {
            LoggerFactory = loggerFactory,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        await foreach (var msg in nats.SubscribeAsync<string>("greet.>"))
        {
            using var activity = msg.StartActivity("Greetings");

            if (msg.Subject.StartsWith("greet.presence"))
            {
                logger.LogInformation("{Data} is here!", msg.Data);

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
