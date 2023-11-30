// ReSharper disable SuggestVarOrType_Elsewhere

using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable SA1515

namespace NATS.Net.DocsExamples.JetStream;

public class PublishPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.JetStream.PublishPage");

        try
        {
            await using var nats1 = new NatsConnection();
            var js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("shop_orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        try
        {
            await using var nats1 = new NatsConnection();
            var js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        {
            #region js
            await using var nats = new NatsConnection();
            var js = new NatsJSContext(nats);

            await js.CreateStreamAsync(new StreamConfig(name: "orders", subjects: new[] { "orders.>" }));
            #endregion
        }

        {
            #region publish
            await using var nats = new NatsConnection();
            var js = new NatsJSContext(nats);

            var order = new Order(OrderId: 1);

            // Use generated JSON serializer
            var orderSerializer = new NatsJsonContextSerializer<Order>(OrderJsonSerializerContext.Default);

            var ack = await js.PublishAsync("orders.new.1", order, serializer: orderSerializer);

            ack.EnsureSuccess();
            #endregion
        }

        {
            await using var nats = new NatsConnection();
            var js = new NatsJSContext(nats);

            var order = new Order(OrderId: 1);

            // Use generated JSON serializer
            var orderSerializer = new NatsJsonContextSerializer<Order>(OrderJsonSerializerContext.Default);

            #region publish-duplicate
            var ack = await js.PublishAsync(subject: "orders.new.1", data: order, opts: new NatsJSPubOpts { MsgId = "1" }, serializer: orderSerializer);
            if (ack.Duplicate)
            {
                // A message with the same ID was published before
            }
            #endregion
        }
    }
}
