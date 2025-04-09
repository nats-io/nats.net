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

Install [NATS.Net](https://www.nuget.org/packages/NATS.Net) from Nuget.

Before we can do anything, we need an Object Store context:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/ObjectStore/IntroPage.cs#obj)]

Let's create our store first. In Object Store, a bucket is simply a storage for key/object pairs:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/ObjectStore/IntroPage.cs#store)]


Now that we have a bucket in our stream, let's see its status using the [NATS command
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

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/ObjectStore/IntroPage.cs#putget)]

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

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/ObjectStore/IntroPage.cs#info)]

### Delete

We can delete a key from a bucket:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/ObjectStore/IntroPage.cs#delete)]

See the API reference for the full list of [Object Store](xref:NATS.Client.ObjectStore.INatsObjStore) operations.
