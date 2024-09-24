// ReSharper disable RedundantAssignment
// ReSharper disable SuggestVarOrType_Elsewhere

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509

namespace NATS.Net.DocsExamples.JetStream;

public class ManagingPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.JetStream.ManagingPage");

        #region js
        await using var nats = new NatsConnection();

        var js = new NatsJSContext(nats);
        #endregion

        try
        {
            await js.DeleteStreamAsync("shop_orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        try
        {
            await js.DeleteStreamAsync("orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region stream
        await js.CreateStreamAsync(new StreamConfig(name: "orders", subjects: new[] { "orders.>" }));
        #endregion

        {
            #region consumer-create
            // Create or get a consumer
            var consumer = await js.CreateOrUpdateConsumerAsync(stream: "orders", new ConsumerConfig("order_processor"));
            #endregion
        }

        {
            #region consumer-get
            // Get an existing consumer
            var consumer = await js.GetConsumerAsync(stream: "orders", consumer: "order_processor");
            #endregion
        }

        {
            #region consumer-durable
            // Create a durable consumer
            var durableConfig = new ConsumerConfig("durable_processor");

            // Same as above
            durableConfig = new ConsumerConfig
            {
                Name = "durable_processor",
                DurableName = "durable_processor",
            };

            var consumer = await js.CreateOrUpdateConsumerAsync(stream: "orders", durableConfig);

            Console.WriteLine($"Consumer Name: {consumer.Info.Name}"); // durable_processor
            Console.WriteLine($"Consumer DurableName: {consumer.Info.Config.DurableName}"); // durable_processor
            #endregion
        }

        {
            #region consumer-ephemeral
            // Create an ephemeral consumer by not setting durable name
            var ephemeralConfig = new ConsumerConfig();

            var consumer = await js.CreateOrUpdateConsumerAsync(stream: "orders", ephemeralConfig);

            Console.WriteLine($"Consumer Name: {consumer.Info.Name}"); // e.g. Z8YlwrP9 (server assigned random name)
            #endregion
        }
    }
}
