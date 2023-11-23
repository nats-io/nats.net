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

public class ConsumerPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.JetStream.ConsumerPage");

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

        #region js
        await using var nats = new NatsConnection();
        var js = new NatsJSContext(nats);

        await js.CreateStreamAsync(new StreamConfig(name: "orders", subjects: new[] { "orders.>" }));

        var consumer = await js.CreateConsumerAsync(stream: "orders", new ConsumerConfig("order_processor"));

        // Use generated JSON serializer
        var orderSerializer = new NatsJsonContextSerializer<Order>(OrderJsonSerializerContext.Default);

        // Publish new order messages
        await nats.PublishAsync(subject: "orders.new.1", data: new Order(OrderId: 1), serializer: orderSerializer);
        #endregion

        {
            #region consumer-next
            var next = await consumer.NextAsync<Order>(serializer: orderSerializer);

            if (next is { } msg)
            {
                Console.WriteLine($"Processing {msg.Subject}: {msg.Data.OrderId}...");
                await msg.AckAsync();
            }
            #endregion
        }

        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var cancellationToken = cts.Token;
            #region consumer-fetch
            // Consume a batch of messages (1000 by default)
            await foreach (var msg in consumer.FetchAsync<Order>(serializer: orderSerializer).WithCancellation(cancellationToken))
            {
                // Process message
                await msg.AckAsync();

                // Loop ends when pull request expires or when requested number of messages (MaxMsgs) received
            }
            #endregion
        }

        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var cancellationToken = cts.Token;
            #region consumer-consume
            // Continuously consume a batch of messages (1000 by default)
            await foreach (var msg in consumer.ConsumeAsync<Order>(serializer: orderSerializer).WithCancellation(cancellationToken))
            {
                // Process message
                await msg.AckAsync();

                // loop never ends unless there is a terminal error, cancellation or a break
            }
            #endregion
        }

        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var cancellationToken = cts.Token;
            #region consumer-consume-error
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await consumer.RefreshAsync(cancellationToken); // or try to recreate consumer

                    await foreach (var msg in consumer.ConsumeAsync<Order>(serializer: orderSerializer).WithCancellation(cancellationToken))
                    {
                        // Process message
                        await msg.AckAsync(cancellationToken: cancellationToken);
                    }
                }
                catch (NatsJSProtocolException e)
                {
                    // log exception
                }
                catch (NatsJSException e)
                {
                    // log exception
                    await Task.Delay(1000, cancellationToken); // backoff
                }
            }
            #endregion
        }
    }
}
