// ReSharper disable RedundantAssignment
// ReSharper disable SuggestVarOrType_Elsewhere

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
        await using var nc = new NatsClient();

        var js = nc.CreateJetStreamContext();
        #endregion

        try
        {
            await js.DeleteStreamAsync("SHOP_ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        try
        {
            await js.DeleteStreamAsync("ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region stream
        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: new[] { "orders.>" }));
        #endregion

        {
            #region consumer-create
            // Create or get a consumer
            var consumer = await js.CreateOrUpdateConsumerAsync(stream: "ORDERS", new ConsumerConfig("order_processor"));
            #endregion
        }

        {
            #region consumer-get
            // Get an existing consumer
            var consumer = await js.GetConsumerAsync(stream: "ORDERS", consumer: "order_processor");
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

            var consumer = await js.CreateOrUpdateConsumerAsync(stream: "ORDERS", durableConfig);

            Console.WriteLine($"Consumer Name: {consumer.Info.Name}"); // durable_processor
            Console.WriteLine($"Consumer DurableName: {consumer.Info.Config.DurableName}"); // durable_processor
            #endregion
        }

        {
            #region consumer-ephemeral
            // Create an ephemeral consumer by not setting durable name
            var ephemeralConfig = new ConsumerConfig();

            var consumer = await js.CreateOrUpdateConsumerAsync(stream: "ORDERS", ephemeralConfig);

            Console.WriteLine($"Consumer Name: {consumer.Info.Name}"); // e.g. Z8YlwrP9 (server assigned random name)
            #endregion
        }
    }
}
