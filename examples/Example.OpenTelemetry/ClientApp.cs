using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Example.OpenTelemetry;

public static class ClientApp
{
    public static async Task Run()
    {
        var serviceName = "ClientApp";
        var serviceVersion = "1.0.0";

        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName: serviceName, serviceVersion: serviceVersion);

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(resourceBuilder)
            .AddNatsClientInstrumentation()
            .AddSource("MyClientSource")
            .Build();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddOtlpExporter()
            .SetResourceBuilder(resourceBuilder)
            .AddNatsClientInstrumentation()
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

        ActivitySource activitySource = new("MyClientSource");

        logger.LogInformation("Client App is starting...");

        await using var nats = new NatsConnection(new NatsOpts
        {
            LoggerFactory = loggerFactory,
            RequestReplyMode = NatsRequestReplyMode.Direct,
        });

        using (var activity = activitySource.StartActivity("SayHi"))
        {
            await nats.PublishAsync("greet.presence.client.app", "ClientApp is here!");

            var response = await nats.RequestAsync<string, string>("greet.hi", "Hi, telemetry!");
            logger.LogInformation("Response: {Response}", response);
        }
    }
}
