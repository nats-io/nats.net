// ReSharper disable SuggestVarOrType_Elsewhere

using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NATS.Client.Serializers.Json;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable SA1515

namespace NATS.Net.DocsExamples.KeyValueStore;

public class IntroPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.KeyValueStore.IntroPage");

        #region kv
        // required to serialize ad-hoc types
        var opts = new NatsOpts { SerializerRegistry = NatsJsonSerializerRegistry.Default };

        await using var nats = new NatsConnection(opts);

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);
        #endregion

        try
        {
            await kv.DeleteStoreAsync("shop_orders");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region store
        var store = await kv.CreateStoreAsync("shop_orders");
        #endregion

        {
            #region put
            await store.PutAsync("order-1", new ShopOrder(Id: 1));

            var entry = await store.GetEntryAsync<ShopOrder>("order-1");

            Console.WriteLine($"[GET] {entry.Value}");
            #endregion
        }

        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var cancellationToken = cts.Token;

            try
            {
                #region watch
                await foreach (var entry in store.WatchAsync<ShopOrder>(cancellationToken: cancellationToken))
                {
                    Console.WriteLine($"[RCV] {entry}");
                }
                #endregion
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    #region order
    public record ShopOrder(int Id);
    #endregion
}
