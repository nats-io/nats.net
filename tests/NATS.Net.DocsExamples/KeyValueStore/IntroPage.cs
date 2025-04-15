// ReSharper disable SuggestVarOrType_Elsewhere

using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

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
        await using NatsClient nc = new NatsClient();
        INatsKVContext kv = nc.CreateKeyValueStoreContext();
        #endregion

        try
        {
            await kv.DeleteStoreAsync("SHOP_ORDERS");
            await Task.Delay(1000);
        }
        catch (NatsJSApiException)
        {
        }

        #region store
        INatsKVStore store = await kv.CreateStoreAsync("SHOP_ORDERS");
        #endregion

        {
            #region put
            await store.PutAsync("order-1", new ShopOrder(Id: 1));

            NatsKVEntry<ShopOrder> entry = await store.GetEntryAsync<ShopOrder>("order-1");

            Console.WriteLine($"[GET] {entry.Value}");
            #endregion
        }

        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            CancellationToken cancellationToken = cts.Token;

            try
            {
                #region watch
                await foreach (NatsKVEntry<ShopOrder> entry in store.WatchAsync<ShopOrder>(cancellationToken: cancellationToken))
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
