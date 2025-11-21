// ReSharper disable SuggestVarOrType_Elsewhere

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable SA1515
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS0168 // Variable is declared but never used

namespace NATS.Net.DocsExamples.JetStream;

public class ConsumerPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.JetStream.ConsumerPage");

        try
        {
            await using NatsConnection nats1 = new NatsConnection();
            NatsJSContext js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        try
        {
            await using NatsConnection nats1 = new NatsConnection();
            NatsJSContext js1 = new NatsJSContext(nats1);
            await js1.DeleteStreamAsync("SHOP_ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region js
        await using NatsClient nc = new NatsClient();
        INatsJSContext js = nc.CreateJetStreamContext();

        await js.CreateStreamAsync(new StreamConfig(name: "ORDERS", subjects: ["orders.>"]));

        INatsJSConsumer consumer = await js.CreateOrUpdateConsumerAsync(stream: "ORDERS", new ConsumerConfig("order_processor"));

        // Publish new order messages
        PubAckResponse ack = await js.PublishAsync(subject: "orders.new.1", data: new Order { Id = 1 });

        // If you want exceptions to be thrown, you can use EnsureSuccess() method instead
        if (!ack.IsSuccess())
        {
            // handle error
        }
        #endregion

        {
            #region consumer-next
            INatsJSMsg<Order>? next = await consumer.NextAsync<Order>();

            if (next is { } msg)
            {
                Console.WriteLine($"Processing {msg.Subject}: {msg.Data.Id}...");
                await msg.AckAsync();
            }
            #endregion
        }

        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            CancellationToken cancellationToken = cts.Token;
            #region consumer-fetch
            await foreach (NatsJSMsg<Order> msg in consumer.FetchAsync<Order>(new NatsJSFetchOpts { MaxMsgs = 1000 }).WithCancellation(cancellationToken))
            {
                // Process message
                await msg.AckAsync();

                // Loop ends when pull request expires or when requested number of messages (MaxMsgs) received
            }
            #endregion
        }

        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            CancellationToken cancellationToken = cts.Token;
            #region consumer-consume
            // Continuously consume a batch of messages (1000 by default)
            await foreach (NatsJSMsg<Order> msg in consumer.ConsumeAsync<Order>().WithCancellation(cancellationToken))
            {
                // Process message
                await msg.AckAsync();

                // loop never ends unless there is a terminal error, cancellation or a break
            }
            #endregion
        }

        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            CancellationToken cancellationToken = cts.Token;

            #region consumer-consume-error
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await consumer.RefreshAsync(cancellationToken); // or try to recreate consumer

                    await foreach (NatsJSMsg<Order> msg in consumer.ConsumeAsync<Order>().WithCancellation(cancellationToken))
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
