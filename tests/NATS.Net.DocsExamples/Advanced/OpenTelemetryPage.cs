// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable IDE0007
#pragma warning disable IDE0008

using System.Diagnostics;
using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Advanced;

public class OpenTelemetryPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Advanced.OpenTelemetryPage");

        {
            #region setup
            // The NATS.Net client uses System.Diagnostics.Activity for tracing.
            // No additional packages are needed to enable tracing — just add an
            // ActivityListener or configure an OpenTelemetry TracerProvider that
            // listens to the "NATS.Net" source.

            // Using OpenTelemetry SDK (install OpenTelemetry and an exporter):
            //
            //   using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            //       .AddSource("NATS.Net")         // listen for NATS activities
            //       .AddSource("MyApp")             // listen for your own activities
            //       .AddOtlpExporter()              // export to Jaeger, Zipkin, etc.
            //       .Build();

            // Or using a plain ActivityListener (no extra packages):
            using ActivityListener listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "NATS.Net",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                    Console.WriteLine($"Started: {activity.OperationName}"),
                ActivityStopped = activity =>
                    Console.WriteLine($"Stopped: {activity.OperationName}"),
            };
            ActivitySource.AddActivityListener(listener);
            #endregion
        }

        {
            #region publish-subscribe
            await using NatsConnection nats = new NatsConnection();

            // Publish and subscribe — activities are created automatically
            await using var sub = await nats.SubscribeCoreAsync<string>("orders.new");
            await nats.PublishAsync("orders.new", "order-123");

            // The message carries trace context in its headers, so the
            // receive activity is automatically linked to the send activity.
            await foreach (NatsMsg<string> msg in sub.Msgs.ReadAllAsync())
            {
                Console.WriteLine($"Received: {msg.Data}");
                break;
            }
            #endregion
        }

        {
            #region custom-activity
            await using NatsConnection nats = new NatsConnection();

            await using var sub = await nats.SubscribeCoreAsync<string>("work.items");
            await nats.PublishAsync("work.items", "item-456");

            await foreach (NatsMsg<string> msg in sub.Msgs.ReadAllAsync())
            {
                // Start a child activity under the message's trace context
                using Activity? activity = msg.StartActivity("ProcessWorkItem");

                // The activity is linked to the original publish span
                Console.WriteLine($"Processing: {msg.Data}");
                break;
            }
            #endregion
        }

        {
            #region filter
            // Filter lets you skip telemetry for specific subjects
            NatsInstrumentationOptions.Default.Filter = context =>
            {
                // Skip internal/health-check subjects
                if (context.Subject.StartsWith("_INBOX."))
                    return false;

                return true;
            };
            #endregion
        }

        {
            #region enrich
            // Enrich lets you add custom tags to every activity
            NatsInstrumentationOptions.Default.Enrich = (activity, context) =>
            {
                activity.SetTag("app.environment", "production");

                if (context.QueueGroup is not null)
                    activity.SetTag("app.queue_group", context.QueueGroup);
            };
            #endregion
        }
    }
}
