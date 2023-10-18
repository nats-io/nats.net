# Object Store

[The Object Store](https://docs.nats.io/nats-concepts/jetstream/obj_store) is very similar to the Key Value Store in that you put and get data using a key.
The difference being that Object store allows for the storage of objects that can be of any size.

Under the covers Object Store is a client side construct that allows you to store and retrieve chunks of data
by a key using JetStream as the stream persistence engine. It's a simple, yet powerful way to store
and retrieve large data like files.

To be able to use Object Store you need to enable JetStream by running the server with `-js` flag e.g. `nats-server -js`.

## Object Store Quick Start

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it with JetStream enabled:

```shell
$ nats-server -js
```

Install `NATS.Client.ObjectStore` preview from Nuget.

Before we can do anything, we need an Object Store context:

```csharp
await using var nats = new NatsConnection();
var js = new NatsJSContext(nats);
var obj = new NatsObjContext(js);
```

Let's create our store first. In Object Store, a bucket is simply a storage for key/object pairs:

```csharp
var store = await obj.CreateObjectStore("test-bucket");
```

Now that we have a KV bucket in our stream, let's see its status using the [NATS command
line client](https://github.com/nats-io/natscli):

```shell
$ nats object ls
╭──────────────────────────────────────────────────────────────────────╮
│                         Object Store Buckets                         │
├─────────────┬─────────────┬─────────────────────┬──────┬─────────────┤
│ Bucket      │ Description │ Created             │ Size │ Last Update │
├─────────────┼─────────────┼─────────────────────┼──────┼─────────────┤
│ test-bucket │             │ 2023-10-18 14:10:27 │ 0 B  │ never       │
╰─────────────┴─────────────┴─────────────────────┴──────┴─────────────╯
```

We can save objects in a bucket by putting them using a key, which is `my/random/data.bin` in our case. We can also retrieve the
saved value by its key:

```csharp
await store.PutAsync("my/random/data.bin", File.OpenRead("data.bin"));

await store.GetAsync("my/random/data.bin", File.OpenWrite("data_copy.bin"));
```

We can also confirm that our value is persisted by using the NATS command line:

```shell
$ nats object info test-bucket my/random/data.bin
Object information for test-bucket > my/random/data.bin

               Size: 10 MiB
  Modification Time: 18 Oct 23 14:54 +0000
             Chunks: 80
             Digest: SHA-256 d34334673e4e2b2300c09550faa5e2b6d0f04245a1d0b664454bb922da56
```

## Other Operations

### Get Info

We can get information about a key in a bucket:

```csharp
var metadata = await store.GetInfoAsync("my/random/data.bin");

Console.WriteLine("Metadata:");
Console.WriteLine($"  Bucket: {metadata.Bucket}");
Console.WriteLine($"  Name: {metadata.Name}");
Console.WriteLine($"  Size: {metadata.Size}");
Console.WriteLine($"  Time: {metadata.MTime}");
Console.WriteLine($"  Chunks: {metadata.Chunks}");

// Outputs:
// Metadata:
//   Bucket: test-bucket
//   Name: my/random/data.bin
//   Size: 10485760
//   Time: 18/10/2023 15:13:22 +00:00
//   Chunks: 80

```

### Delete

We can delete a key from a bucket:

```csharp
await store.DeleteAsync("my/random/data.bin");
```
