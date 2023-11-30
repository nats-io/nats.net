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
    }
}
