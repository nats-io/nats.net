# Key/Value Store

[Key/Value Store](https://docs.nats.io/nats-concepts/jetstream/key-value-store) allows client applications to create
'buckets' and use them as immediately consistent, persistent associative arrays.

Under the covers KV is a client side construct that allows you to store and retrieve values by a key using JetStream as
the stream persistence engine. It's a simple, yet powerful way to store and retrieve data.

To be able to use KV you need to enable JetStream by running the server with `-js` flag e.g. `nats-server -js`.

## Key/Value Store Quick Start

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it with JetStream enabled:

```shell
$ nats-server -js
```

Install `NATS.Client.KeyValueStore` preview from Nuget.

Before we can do anything, we need a Key/Value Store context:

```csharp
await using var nats = new NatsConnection();
var js = new NatsJSContext(nats);
var kv = new NatsKVContext(js);
```

Let's create our store first. In Key/Value Store, a bucket is simply a storage for key/value pairs:

```csharp
var store = await kv.CreateStoreAsync("shop_orders");
```

Now that we have a KV bucket in our stream, let's see its status using the [NATS command
line client](https://github.com/nats-io/natscli):

```shell
$ nats kv ls
╭───────────────────────────────────────────────────────────────────────────────╮
│                               Key-Value Buckets                               │
├─────────────┬─────────────┬─────────────────────┬──────┬────────┬─────────────┤
│ Bucket      │ Description │ Created             │ Size │ Values │ Last Update │
├─────────────┼─────────────┼─────────────────────┼──────┼────────┼─────────────┤
│ shop_orders │             │ 2023-10-12 15:29:40 │ 0 B  │ 0      │ never       │
╰─────────────┴─────────────┴─────────────────────┴──────┴────────┴─────────────╯
```

We can save values in a bucket by putting them using a key, which is `order-1` in our case. We can also retrieve the
saved value by its key:

```csharp
await store.PutAsync("order-1", new ShopOrder(Id: 1));

var entry = await store.GetEntryAsync<ShopOrder>("order-1");

Console.WriteLine($"[GET] {entry.Value}");

public record ShopOrder(int Id);
```

We can also confirm that our value is persisted by using the NATS command line:

```shell
$ nats kv get shop_orders order-1
shop_orders > order-1 created @ 12 Oct 23 15:31 UTC

{"Id":1}
```

## Key/Value Store Watchers

Key/Value Store supports watchers that allow you to be notified when a value is added, updated or deleted from a
bucket. Let's see how we can use watchers to be notified when a value is added to our bucket:

```csharp
await foreach (var entry in store.WatchAllAsync<ShopOrder>())
{
    Console.WriteLine($"[RCV] {entry}");
}

// Outputs:
// [RCV] NatsKVEntry { Bucket = shop_orders, Key = order-1, Value = ShopOrder { Id = 1 }, Revision = 1, Delta = 0,
//       Created = 12/10/2023 15:31:51 +00:00, Operation = Put }
```
