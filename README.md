# AlterNats
[![GitHub Actions](https://github.com/Cysharp/AlterNats/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/AlterNats/actions) [![Releases](https://img.shields.io/github/release/Cysharp/AlterNats.svg)](https://github.com/Cysharp/AlterNats/releases)

An alternative high performance [NATS](https://nats.io/) client for .NET. Zero Allocation and Zero Copy Architecture to achive x2~4 performance compare with [official NATS client](https://github.com/nats-io/nats.net) and [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)'s PubSub.

![image](https://user-images.githubusercontent.com/46207/164392256-46d09111-ec70-4cf3-b33d-38dc5d258455.png)

The most major PubSub Solution in C# is using Redis and StackExchange.Redis. Redis also has a managed service and has a well-designed client library. However, Redis is primarily a KVS and functions poorly as a PubSub.

* Lack of monitoring for PubSub
* Lack of clustering support for PubSub
* Unbalanced pricing for managed services(not much memory is needed for PubSub)
* Performance

Since NATS is specialized for PubSub, it has a rich system for that purpose, and its performance seems to be perfect. The only drawback is that there is no managed service, but if you use NATS as a pure PubSub, you do not need to think about the persistence process, so I think it is one of the easiest middlewares to operate.

[nats.net](https://github.com/nats-io/nats.net) works well enough, but the API has some weirdness that is not C#-like. The reason for this is that it is a port from Go, as noted in the documentation.

> The NATS C# .NET client was originally developed with the idea in mind that it would support the .NET 4.0 code base for increased adoption, and closely parallel the GO client (internally) for maintenance purposes. So, some of the nice .NET APIs/features were intentionally left out.

AlterNats pursues high performance by utilizing a C#-like API and the latest API in .NET 6. See the [Performance](#performance) section for architectural innovations related to performance.

Getting Started
---
Package is published on NuGet.

> PM> Install-Package [AlterNats](https://www.nuget.org/packages/AlterNats)

Here is the simple example.

```csharp
// create connection(default, connect to nats://localhost:4222)
await using var conn = new NatsConnection();

// for subscriber. await register to NATS server(not means await complete)
var subscription = await conn.SubscribeAsync<Person>("foo", x =>
{
    Console.WriteLine($"Received {x}");
});

// for publisher.
await conn.PublishAsync("foo", new Person(30, "bar"));

// unsubscribe
subscription.Dipose();
```

All value is automatically serialized via `System.Text.Json`. If you want to more performance, you can use [MessagePack for C#](https://github.com/neuecc/MessagePack-CSharp) as builtin serializer(requires `AlterNats.MessagePack` package).

Configure option adopts immutable options with `with` operator.
 
```csharp
// Options is immutable and can configure `with` operator
var options = NatsOptions.Default with
{
    Url = "nats://127.0.0.1:9999",
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information),
    Serializer = new MessagePackNatsSerializer(),
    ConnectOptions = ConnectOptions.Default with
    {
        Echo = true,
        Username = "foo",
        Password = "bar",
    }
};

await using var conn = new NatsConnection(options);
```

Connections is thread-safe, physically connected as one connection to one connection, and all commands are automatically multiplexed. Therefore, connection is usually held as singleton. Connection is automatically connected on the first call and automatically reconnected if disconnected.

In addition to keeping it in a static field, it can be used integrated with a generic host by the `AlterNats.Hosting` package when registered with a DI.

> PM> Install-Package [AlterNats](https://www.nuget.org/packages/AlterNats.Hosting)

```csharp
using AlterNats;

var builder = WebApplication.CreateBuilder(args);

// Register NatsConnectionPool, NatsConnection, INatsCommand to ServiceCollection
builder.Services.AddNats();

var app = builder.Build();

app.MapGet("/subscribe", (INatsCommand command) => command.SubscribeAsync("foo", (int x) => Console.WriteLine($"received {x}")));
app.MapGet("/publish", (INatsCommand command) => command.PublishAsync("foo", 99));

app.Run();
```

`AddNats` register NatsConnection and setup correct loggerfactory.

Minimum RPC
---
Nats supports request/response protocol, it will become simple RPC.

```csharp
// Server
await connection.SubscribeRequestAsync<FooRequest, FooResponse>("foobar.key", req =>
{
    return new FooResponse();
});

// Client
var response = await connection.RequestAsync<FooRequest, FooResponse>("foobar.key", new FooRequest());
```

Logging
---
In default, internal error is not logged so recommend to setup LoggerFactory. AlterNats has builtin simple `MinimumConsoleLoggerFactory`.

```csharp
var options = NatsOptions.Default with
{
    LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Information)
};
```

However for production use, use `Microsoft.Extensions.Logging` and setup your favorite logger. [Cysharp/ZLogger](https://github.com/Cysharp/ZLogger/) is especially recommended to keep high performance.

```csharp
// You're using Generic Host(Microsoft.Extensions.Hositng) or ASP.NET Core, you can get ILoggerFactory from the built-in pipeline.
// Otherwise, build it yourself from ServiceCollection.
var provider = new ServiceCollection()
    .AddLogging(x =>
    {
        x.ClearProviders();
        x.SetMinimumLevel(LogLevel.Information);
        x.AddZLoggerConsole();
    })
    .BuildServiceProvider();

var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

var options = NatsOptions.Default with
{
    LoggerFactory = loggerFactory
};

await using var connection = new NatsConnection(options);
```

Integrated with generic host(ApplicationBuilder), `AddNats` automatically setup logger-factory.

```csharp
var builder = WebApplication.CreateBuilder(args);

// will setup ApplicationBuilder's LoggerFactory
builder.Services.AddNats();
```

NatsConnection
---
NatsConnection is 1:1 physical socket connection of nats. It will connect automatically when first operation called. The connection can also be initiated explicitly with `ConnectAsync`. Executing `DisposeAsync` disconnects the connection. If the network is disconnected, it will automatically reconnect.

```csharp
public class NatsConnection : IAsyncDisposable, INatsCommand
{
    public NatsConnection()
        : this(NatsOptions.Default)
    public NatsConnection(NatsOptions options)
    
    public NatsOptions Options { get; }
    public NatsConnectionState ConnectionState { get; }
    public ServerInfo? ServerInfo { get; }
    public async ValueTask ConnectAsync()
    public NatsStats GetStats()
    public async ValueTask DisposeAsync()
}
```

## INatsCommand

INatsCommand is operation commands of NATS. key is available in `NatsKey` and `string` variants, see the [NatsKey](#natskey) section for details.

Post*** is fire-and-forget command, ***Async is await network send.

```csharp
public interface INatsCommand
{
    IObservable<T> AsObservable<T>(in NatsKey key);
    IObservable<T> AsObservable<T>(string key);
    ValueTask FlushAsync();
    ValueTask<TimeSpan> PingAsync();
    void PostDirectWrite(byte[] protocol);
    void PostDirectWrite(DirectWriteCommand command);
    void PostDirectWrite(string protocol, int repeatCount = 1);
    void PostPing();
    void PostPublish(in NatsKey key);
    void PostPublish(in NatsKey key, byte[] value);
    void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value);
    void PostPublish(string key);
    void PostPublish(string key, byte[] value);
    void PostPublish(string key, ReadOnlyMemory<byte> value);
    void PostPublish<T>(in NatsKey key, T value);
    void PostPublish<T>(string key, T value);
    void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values);
    void PostPublishBatch<T>(IEnumerable<(string, T?)> values);
    ValueTask PublishAsync(in NatsKey key);
    ValueTask PublishAsync(in NatsKey key, byte[] value);
    ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value);
    ValueTask PublishAsync(string key);
    ValueTask PublishAsync(string key, byte[] value);
    ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value);
    ValueTask PublishAsync<T>(in NatsKey key, T value);
    ValueTask PublishAsync<T>(string key, T value);
    ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values);
    ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler);
    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(NatsKey key, TRequest request, CancellationToken cancellationToken = default);
    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request, CancellationToken cancellationToken = default);
    ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler);
    ValueTask<IDisposable> SubscribeAsync(string key, Action handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler);
}
```

`AsObservable` is similar as subscribe but handler will be `IObservable<T>`.

Publish(`byte[]`) or Publish(`ReadOnlyMemory<byte>`) is special, in these cases, the value is sent as is, without passing through the serializer.

## Hook

`NatsConnection` has some hook events.

```csharp
public event EventHandler<string>? ConnectionDisconnected;
public event EventHandler<string>? ConnectionOpened;
public event EventHandler<string>? ReconnectFailed;
public Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync;
```

`ConnectionOpened`, `ConnectionDisconnected`, `ReconnectFailed` is called when occurs there event. `OnConnectingAsync` is called before connect to NATS server. For example, check health by HTTP before connect server as TCP.

```csharp
// NATS server requires `-m 8222` option
await using var conn = new NatsConnection();
conn.OnConnectingAsync = async x => // (host, port)
{
    var health = await new HttpClient().GetFromJsonAsync<NatsHealth>($"http://{x.Host}:8222/healthz");
    if (health == null || health.status != "ok") throw new Exception();

    // if returning another (host, port), TCP connection will use it.
    return x;
};

public record NatsHealth(string status);
```

### Stats

You can monitor network stats by `NatsConnection.GetStats()`. It can get these stats counter.

```csharp
public readonly record struct NatsStats
(
    long SentBytes,
    long ReceivedBytes,
    long PendingMessages,
    long SentMessages,
    long ReceivedMessages,
    long SubscriptionCount
);
```

NatsConnectionPool
---
`NatsConnection` is 1:1 physical connection and internally create single pair of read/write async loop. Single connections are effective for multiplexing, but when flow rates are high, being a single loop is a negative performance factor.

`NatsConnectionPool` is a simple round-robin connection pool that manages multiple connections internally.

```csharp
public sealed class NatsConnectionPool : IAsyncDisposable
{
    public NatsConnectionPool()
        : this(Environment.ProcessorCount / 2, NatsOptions.Default)
    public NatsConnectionPool(int poolSize)
        : this(poolSize, NatsOptions.Default)
    public NatsConnectionPool(NatsOptions options)
        : this(Environment.ProcessorCount / 2, options)
    public NatsConnectionPool(int poolSize, NatsOptions options)

    public IEnumerable<NatsConnection> GetConnections()
    public NatsConnection GetConnection()
    public INatsCommand GetCommand()
```

By default, it creates `ProcessorCount / 2` connections and returns a different connection in a round-robin each time `GetConnection()` or `GetCommand()` is called.

If you are using `AlterNats.Hosting`, `AddNats(poolSize)` will use `NatsConnectionPool`.

```csharp
builder.Services.AddNats(poolSize: 4);

// DI injection can get INatsCommand(or NatsConnection) that call pool.Command() as transient.
public MyController(INatsCommand command)
```

Sharding
---
Supports horizontal sharding on the client side using Key. If you are using Sharding, be aware that you cannot use wildcards for Key.

```csharp
public sealed class NatsShardingConnection : IAsyncDisposable
{
    public NatsShardingConnection(int poolSize, NatsOptions options, string[] urls)

    public IEnumerable<NatsConnection> GetConnections()
    public ShardringNatsCommand GetCommand(in NatsKey key)
    public ShardringNatsCommand GetCommand(string key)
}
```

If you are using `AlterNats.Hosting`, `AddNats(string[] urls)` will use `NatsShardingConnection`.

```csharp
builder.Services.AddNats(poolSize: 4, urls: new[]{"nats://foo,nats://bar"});

// DI injection can get NatsShardingConnection as singleton
public MyController(NatsShardingConnection connection)
{
    var cmd = connection.GetCommand("foobarbaz");
}
```

NatsKey
---
NatsKey is encoded key string. If you store string key as const, use NatsKey instead achieves better performance.

```csharp
public static class Keys
{
    public static readonly NatsKey FooKey = new NatsKey("foo");
}
```

NatsOptions
---
NatsOptions is configure behaviour of AlterNats.

```csharp
public sealed record NatsOptions
(
    string Url,
    ConnectOptions ConnectOptions,
    TlsOptions: TlsOptions,
    INatsSerializer Serializer,
    ILoggerFactory LoggerFactory,
    int WriterBufferSize,
    int ReaderBufferSize,
    bool UseThreadPoolCallback,
    string InboxPrefix,
    bool NoRandomize,
    TimeSpan PingInterval,
    int MaxPingOut,
    TimeSpan ReconnectWait,
    TimeSpan ReconnectJitter,
    TimeSpan ConnectTimeout,
    int CommandPoolSize,
    TimeSpan RequestTimeout
)
    
public static NatsOptions Default = new NatsOptions(
    Url: "nats://localhost:4222",
    ConnectOptions: ConnectOptions.Default,
    TlsOptions: TlsOptions.Default,
    Serializer: new JsonNatsSerializer(new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
    LoggerFactory: NullLoggerFactory.Instance,
    WriterBufferSize: 65534,
    ReaderBufferSize: 1048576,
    UseThreadPoolCallback: false,
    InboxPrefix: "_INBOX.",
    NoRandomize: false,
    PingInterval: TimeSpan.FromMinutes(2),
    MaxPingOut: 2,
    ReconnectWait: TimeSpan.FromSeconds(2),
    ReconnectJitter: TimeSpan.FromMilliseconds(100),
    ConnectTimeout: TimeSpan.FromSeconds(2),
    CommandPoolSize: 256,
    RequestTimeout: TimeSpan.FromMinutes(1)
)
```

ConnectOptions
---
ConnectOptions is client -> server configuration of NATS.

```csharp
public sealed record ConnectOptions
{
    /// <summary>Optional boolean. If set to true, the server (version 1.2.0+) will not send originating messages from this connection to its own subscriptions. Clients should set this to true only for server supporting this feature, which is when proto in the INFO protocol is set to at least 1.</summary>
    [JsonPropertyName("echo")]
    public bool Echo { get; init; } = true;

    /// <summary>Turns on +OK protocol acknowledgements.</summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    /// <summary>Turns on additional strict format checking, e.g. for properly formed subjects</summary>
    [JsonPropertyName("pedantic")]
    public bool Pedantic { get; init; }

    /// <summary>Indicates whether the client requires an SSL connection.</summary>
    [JsonPropertyName("tls_required")]
    public bool TLSRequired { get; init; }

    [JsonPropertyName("nkey")]
    public string? Nkey { get; init; } = null;

    /// <summary>The JWT that identifies a user permissions and acccount.</summary>
    [JsonPropertyName("jwt")]
    public string? JWT { get; init; } = null;

    /// <summary>In case the server has responded with a nonce on INFO, then a NATS client must use this field to reply with the signed nonce.</summary>
    [JsonPropertyName("sig")]
    public string? Sig { get; init; } = null;

    /// <summary>Client authorization token (if auth_required is set)</summary>
    [JsonPropertyName("auth_token")]
    public string? AuthToken { get; init; } = null;

    /// <summary>Connection username (if auth_required is set)</summary>
    [JsonPropertyName("user")]
    public string? Username { get; init; } = null;

    /// <summary>Connection password (if auth_required is set)</summary>
    [JsonPropertyName("pass")]
    public string? Password { get; init; } = null;

    /// <summary>Optional client name</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; } = null;

    /// <summary>The implementation language of the client.</summary>
    [JsonPropertyName("lang")]
    public string ClientLang { get; init; } = "C#";

    /// <summary>The version of the client.</summary>
    [JsonPropertyName("version")]
    public string ClientVersion { get; init; } = GetAssemblyVersion();

    /// <summary>optional int. Sending 0 (or absent) indicates client supports original protocol. Sending 1 indicates that the client supports dynamic reconfiguration of cluster topology changes by asynchronously receiving INFO messages with known servers it can reconnect to.</summary>
    [JsonPropertyName("protocol")]
    public int Protocol { get; init; } = 1;

    [JsonPropertyName("account")]
    public string? Account { get; init; } = null;

    [JsonPropertyName("new_account")]
    public bool? AccountNew { get; init; }

    [JsonPropertyName("headers")]
    public bool Headers { get; init; } = false;

    [JsonPropertyName("no_responders")]
    public bool NoResponders { get; init; } = false;
}
```

TlsOptions
---
TlsOptions are options for TLS connections.  These options do not apply to WebSocket connections.

```csharp
public sealed record ConnectOptions
{
    /// <summary>Path to PEM-encoded X509 Certificate</summary>
    public string? CertFile { get; init; }

    /// <summary>Path to PEM-encoded Private Key</summary>
    public string? KeyFile { get; init; }

    /// <summary>Path to PEM-encoded X509 CA Certificate</summary>
    public string? CaFile { get; init; }

    /// <summary>When true, disable TLS</summary>
    public bool Disabled { get; init; }

    /// <summary>When true, skip remote certificate verification and accept any server certificate</summary>
    public bool InsecureSkipVerify { get; init; }
}
```


Serialization
---
In default, serializer uses System.Text.Json. for more performant serialization, you can use MessagePack extension package.

> PM> Install-Package [AlterNats.MessagePack](https://www.nuget.org/packages/AlterNats.MessagePack)

```csharp
var options = NatsOptions.Default with
{
    Serializer = new MessagePackNatsSerializer(),
};
```

Performance
---

### Binary compare of TextProtocol

[NATS Protocol](https://docs.nats.io/reference/reference-protocols/nats-protocol) is text-protocol, However, by parsing it like binary code, it provides a fast decision.

NATS can determine the type of message coming in by the leading string (`INFO`, `MSG`, `PING`, `+OK`, `-ERR`, etc.). It can be converted to a 4-character Int for comparison.

```csharp
// msg = ReadOnlySpan<byte>
if(Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference<byte>(msg)) == 1330007625) // INFO
{
}
```

AlterNats using this constant.

```csharp
internal static class ServerOpCodes
{
    public const int Info = 1330007625;  // Encoding.ASCII.GetBytes("INFO") |> MemoryMarshal.Read<int>
    public const int Msg = 541545293;    // Encoding.ASCII.GetBytes("MSG ") |> MemoryMarshal.Read<int>
    public const int Ping = 1196312912;  // Encoding.ASCII.GetBytes("PING") |> MemoryMarshal.Read<int>
    public const int Pong = 1196314448;  // Encoding.ASCII.GetBytes("PONG") |> MemoryMarshal.Read<int>
    public const int Ok = 223039275;     // Encoding.ASCII.GetBytes("+OK\r") |> MemoryMarshal.Read<int>
    public const int Error = 1381123373; // Encoding.ASCII.GetBytes("-ERR") |> MemoryMarshal.Read<int>
}
```

### Automatically pipelining

All NATS protocol writes and reads are pipelined (batch).

![image](https://user-images.githubusercontent.com/46207/167585601-5634057e-812d-4b60-ab5b-61d9c8c37063.png)

This is not only effective in reducing round trip time, but also in reducing the number of consecutive system calls.

### All one object

For the convenience of packing into a Channel, we need to put the data into a write message object and hold it in the heap. We also need a Promise for an asynchronous method that waits until the write is complete.

```csharp
await connection.PublishAsync(value);
```

To implement such an API efficiently, let's cohabitate and pack all the functions into a single message object (internally named Command) that must be allocated.

```csharp
class AsyncPublishCommand<T> : ICommand, IValueTaskSource, IThreadPoolWorkItem, IObjectPoolNode<AsyncPublishCommand<T>>

internal interface ICommand
{
    void Write(ProtocolWriter writer);
}

internal interface IObjectPoolNode<T>
{
    ref T? NextNode { get; }
}
```

And by putting this one object itself into the object pool, it is asynchronous programming with zero allocation.

### Zero-copy Architecture

The data to be Publish/Subscribe is usually serialized C# types to JSON, MessagePack, and so on. In this case, it is inevitably exchanged in `byte[]`, for example, the content of `RedisValue` in StackExchange.Redis is actually byte[], and whether sending or receiving, `byte[]` will be generated and retained.

AlterNats serializer uses `IBufferWriter<byte>` for Write, `ReadOnlySequence<byte>` for Read.

```csharp
public interface INatsSerializer
{
    int Serialize<T>(ICountableBufferWriter bufferWriter, T? value);
    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}

public interface ICountableBufferWriter : IBufferWriter<byte>
{
    int WrittenCount { get; }
}
```

```csharp
// Implementation of MessagePack for C#
public class MessagePackNatsSerializer : INatsSerializer
{
    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        var before = bufferWriter.WrittenCount;
        MessagePackSerializer.Serialize(bufferWriter, value);
        return bufferWriter.WrittenCount - before;
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        return MessagePackSerializer.Deserialize<T>(buffer);
    }
}
```

The Serialize methods of System.Text.Json and MessagePack for C# provide an overload that accepts `IBufferWriter<byte>`. The serializer directly accesses and writes to the buffer provided for writing to the Socket via `IBufferWriter<byte>`, eliminating the copying of bytes[] between the Socket and the serializer.

![image](https://user-images.githubusercontent.com/46207/167587816-c50b0af3-edaa-4a2a-b536-67aed0a5f908.png)

Tips
---
As mentioned in the [NatsConnectionPool](#natsconnectionpool) section, the connection is driven by a pair of single async read/write loop. Although it can be a balance with multiplexing, proper use of NatsConnectionPool can improve performance.

The Subscribe handler also runs on the read-loop, so if the handler blocks, the read-loop will also block. Be sure to use await when I/O, etc. occurs, and consider a separate Task.Run for CPU-intensive callbacks.

If `NatsOptions.UseThreadPoolCallback` is set to true, all callback handlers are executed on the thread pool, so the read loop is not blocked. However, performance under high workloads is reduced compared to the false case. Therefore, the default is false.

Architecture guide
---

Cysharp creating [MagicOnion](https://github.com/Cysharp/MagicOnion) network framework that run both .NET and Unity. For example, with Unity (or other .NET Client) and Server (MagicOnion).

![image](https://user-images.githubusercontent.com/46207/164406771-58318153-c6a7-49c0-b3af-2b8389e2c9c1.png)

Single server is simple, however real-case needs multiple server. This pattern uses loadbalancer and pubsub backend.

![image](https://user-images.githubusercontent.com/46207/164409016-b6e99f36-bdf7-47a9-80a6-558010963a36.png)

This pattern is used in [Socket.IO](https://socket.io/)'s Redis adaptor, [SignalR](https://docs.microsoft.com/ja-jp/aspnet/signalr/overview/getting-started/introduction-to-signalr)'s Redis backplane, and also MagicOnion's Redis backplane. What you can do with PubSub in Redis, you can do with NATS.

This pattern can be server as stateless and easy to scale-out. The disadvantage is that the server cannot have state. If states are required, there are architectures that let you connect to specific servers.

![image](https://user-images.githubusercontent.com/46207/164417937-7d1adedb-36ee-453b-9ca6-9d41aded50af.png)

In this case, you can have full in-memory state in the server to connect to the same server, or you can run a so-called game loop inside to process the game. You can also host a headless Unity or similar without a screen, and run the client itself on the server.

However, it is difficult to achieve this pattern with plain Kubernetes, and there is a solution called [Agones](https://agones.dev/site/) for this purpose.

However, this has its drawbacks. The game server that Agones envisions is for hosting one game session per process, so it cannot be used as is to host many game sessions in a single process. A lightweight game server (MagicOnion) can pack many game sessions into a single process, and there is a big difference in cost if this is set up in separate containers.

Cysharp is providing state-full server utility for game-loop called [LogicLooper](https://github.com/Cysharp/LogicLooper). Hosting LogicLooper as [Worker Service](https://docs.microsoft.com/en-us/dotnet/core/extensions/workers) in .NET 6 will be simpler pattern.

![image](https://user-images.githubusercontent.com/46207/164417734-f2ec80e7-f12f-4a84-8252-ce28f9b53f05.png)

Since the state of the entire game is managed by LogicLooper itself, MagicOnion itself, which is directly connected to the client, is stateless. Therefore, from an infrastructure standpoint, MagicOnion only needs to be placed under a load balancer, and all the hassles associated with connections between servers can be pushed to NATS, making infrastructure management itself a fairly simple configuration.

Also, MagicOnion itself is a stateful system, and it is easy for each user to have their own state (as long as they do not cross servers). Therefore, data that arrives from LogicLooper and does not need to be delivered to connected users can be culled using the user states that MagicOnion has, and the amount of traffic can be reduced by not transferring the data in the first place or by thinning it out, thereby improving the user experience. This improves the user experience.

This pattern is ideal for MORPGs, MMORPGs and creating Metaverse.

Limitation
---
Currently, AlterNats is not oriented toward compatibility and comprehensiveness, specializes in performance against Core NATS. Therefore, these features are not supported.

* JetStream
* Header

License
---
This library is licensed under the MIT License.
