# Serialization

NATS .NET Client supports serialization of messages using a simple interface [`INatsSerializer`](xref:NATS.Client.Core.INatsSerializer).

```csharp
public interface INatsSerializer
{
    // Serialize the value to the buffer.
    void Serialize<T>(IBufferWriter<byte> bufferWriter, T value);

    // Deserialize the value from the buffer.
    T? Deserialize<T>(in ReadOnlySequence<byte> buffer);
}
```

By default, the client uses the [`NatsDefaultSerializer`](xref:NATS.Client.Core.NatsDefaultSerializer) which can handle binary data, UTF8 strings and numbers. You can provide your own
serializer by implementing the [`INatsSerializer`](xref:NATS.Client.Core.INatsSerializer) interface or using the [`NatsJsonContextSerializer`](xref:NATS.Client.Core.NatsJsonContextSerializer) for generated
JSON serialization. Serializers can also be chained together to provide multiple serialization formats typically
depending on the types being used.

## Default Serializer

Default serializer is used when no serializer is provided to the connection options. It can handle binary data, UTF8
strings and numbers. It uses the following rules to determine the type of the data:

- If the data is a byte array, [`Memory<byte>`](https://learn.microsoft.com/dotnet/api/system.memory-1), [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1) or similar it is treated as binary data.
- If the data is a string or similar it is treated as UTF8 string.
- If the data is a primitive (for example `DateTime`, `int` or `double`. See also [`NatsUtf8PrimitivesSerializer`](xref:NATS.Client.Core.NatsUtf8PrimitivesSerializer)) it is treated as the primitive encoded as a UTF8 string.
- For any other type, the serializer will throw an exception.

```csharp
// Same as not specifying a serializer.
var natsOpts = NatsOpts.Default with { Serializer = NatsDefaultSerializer.Default };

await using var nats = new NatsConnection(natsOpts);

await using INatsSub<string> sub = await nats.SubscribeAsync<string>(subject: "foo");

// Flush the the network buffers to make sure the subscription request has been processed.
await nats.PingAsync();

await nats.PublishAsync<string>(subject: "foo", data: "Hello World");

NatsMsg<string?> msg = await sub.Msgs.ReadAsync();

// Outputs 'Hello World'
Console.WriteLine(msg.Data);
```

The default serializer is designed to be used by developers who want to only work with binary data, and provide an out
of the box experience for basic use cases like sending and receiving UTF8 strings.

### Using JSON Serialization with Reflection

If you're not using [Native AOT deployments](https://learn.microsoft.com/dotnet/core/deploying/native-aot) you can use
the [`NatsJsonSerializer`](xref:NATS.Client.Core.Serializers.Json.NatsJsonSerializer) to serialize and deserialize
messages. [`NatsJsonSerializer`](xref:NATS.Client.Core.Serializers.Json.NatsJsonSerializer) uses [`System.Text.Json`](https://learn.microsoft.com/dotnet/api/system.text.json)
APIs that can work with types that are not registered to generate serialization code.

Using this serializer is most useful for use cases where you want to send and receive JSON messages and you don't want to
worry about registering types. It's also useful for prototyping and testing. To use the serializer you need to install
the `NATS.Client.Serializers.Json` Nuget package.

```shell
$ dotnet add package NATS.Client.Serializers.Json --prerelease
```

Then set the serializer as the default for the connection:

```csharp
using NATS.Client.Serializers.Json;

var natsOpts = NatsOpts.Default with { Serializer = NatsJsonSerializer.Default };

await using var nats = new NatsConnection(natsOpts);
```

## Using JSON Serializer Context

The [`NatsJsonContextSerializer`](xref:NATS.Client.Core.NatsJsonContextSerializer) uses the [`System.Text.Json`](https://learn.microsoft.com/dotnet/api/system.text.json) serializer to serialize and deserialize messages. It relies
on the [`System.Text.Json` source generator](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-source-generator/)
to generate the serialization code at compile time. This is the recommended JSON serializer for most use cases and it's
required for [Native AOT deployments](https://learn.microsoft.com/dotnet/core/deploying/native-aot).

First you need to define your JSON classes and a context to generate the serialization code:
```csharp
public record MyData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

[JsonSerializable(typeof(MyData))]
internal partial class MyJsonContext : JsonSerializerContext;
```

Then you can use the [`NatsJsonContextSerializer`](xref:NATS.Client.Core.NatsJsonContextSerializer) to serialize and deserialize messages:
```csharp
// Set the custom serializer as the default for the connection.
var natsOpts = NatsOpts.Default with { Serializer = new NatsJsonContextSerializer(MyJsonContext.Default) };

await using var nats = new NatsConnection(natsOpts);

await using INatsSub<MyData> sub = await nats.SubscribeAsync<MyData>(subject: "foo");

// Flush the the network buffers to make sure the subscription request has been processed.
await nats.PingAsync();

await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" });

NatsMsg<MyData?> msg = await sub.Msgs.ReadAsync();

// Outputs 'MyData { Id = 1, Name = bar }'
Console.WriteLine(msg.Data);
```

You can also set the serializer for a specific subscription or publish call:
```csharp
await using var nats = new NatsConnection();

var natsSubOpts = new NatsSubOpts { Serializer = new NatsJsonContextSerializer(MyJsonContext.Default) };
await using INatsSub<MyData> sub = await nats.SubscribeAsync<MyData>(subject: "foo", opts: natsSubOpts);

// Flush the the network buffers to make sure the subscription request has been processed.
await nats.PingAsync();

var natsPubOpts = new NatsPubOpts { Serializer = new NatsJsonContextSerializer(MyJsonContext.Default) };
await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" }, opts: natsPubOpts);

NatsMsg<MyData?> msg = await sub.Msgs.ReadAsync();

// Outputs 'MyData { Id = 1, Name = bar }'
Console.WriteLine(msg.Data);
```

## Using Custom Serializer

You can also provide your own serializer by implementing the [`INatsSerializer`](xref:NATS.Client.Core.INatsSerializer) interface. This is useful if you need to
support a custom serialization format or if you need to support multiple serialization formats.

Here is an example of a custom serializer that uses the Google ProtoBuf serializer to serialize and deserialize:

```csharp
public class MyProtoBufSerializer : INatsSerializer
{
    public static readonly INatsSerializer Default = new MyProtoBufSerializer();

    public INatsSerializer? Next => default;

    public void Serialize<T>(IBufferWriter<byte> bufferWriter, T value)
    {
        if (value is IMessage message)
        {
            message.WriteTo(bufferWriter);
        }
        else
        {
            throw new NatsException($"Can't serialize {typeof(T)}");
        }
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(Greeting))
        {
            return (T)(object)Greeting.Parser.ParseFrom(buffer);
        }

        throw new NatsException($"Can't deserialize {typeof(T)}");
    }
}
```

You can then use the custom serializer as the default for the connection:

```csharp
var natsOpts = NatsOpts.Default with { Serializer = MyProtoBufSerializer.Default };

await using var nats = new NatsConnection(natsOpts);

await using var sub = await nats.SubscribeAsync<Greeting>(subject: "foo");

// Flush the the network buffers to make sure the subscription request has been processed.
await nats.PingAsync();

await nats.PublishAsync(subject: "foo", data: new Greeting { Id = 42, Name = "Marvin" });

var msg = await sub.Msgs.ReadAsync();

// Outputs '{ "id": 42, "name": "Marvin" }'
Console.WriteLine(msg.Data);
```

## Using Multiple Serializers (chaining)

You can also chain multiple serializers together to support multiple serialization formats. The first serializer in the
chain that can handle the data will be used. This is useful if you need to support multiple serialization formats and
reuse them.

Note that chaining serializers is implemented by convention and not enforced by the [`INatsSerializer`](xref:NATS.Client.Core.INatsSerializer)
interface since the next serializer would not be exposed to external users of the interface.

Here is an example of a serializer that uses the Google ProtoBuf serializer and the [`NatsJsonContextSerializer`](xref:NATS.Client.Core.NatsJsonContextSerializer) to
serialize and deserialize messages based on the type:

```csharp
var serializers = new NatsJsonContextSerializer(MyJsonContext.Default, next: MyProtoBufSerializer.Default);
var natsOpts = NatsOpts.Default with { Serializer = serializers };

await using var nats = new NatsConnection(natsOpts);

await using var sub1 = await nats.SubscribeAsync<Greeting>(subject: "greet");
await using var sub2 = await nats.SubscribeAsync<MyData>(subject: "data");

// Flush the the network buffers to make sure the subscription request has been processed.
await nats.PingAsync();

await nats.PublishAsync(subject: "greet", data: new Greeting { Id = 42, Name = "Marvin" });
await nats.PublishAsync(subject: "data", data: new MyData { Id = 1, Name = "Bob" });

var msg1 = await sub1.Msgs.ReadAsync();
var msg2 = await sub2.Msgs.ReadAsync();

// Outputs '{ "id": 42, "name": "Marvin" }'
Console.WriteLine(msg1.Data);

// Outputs 'MyData { Id = 1, Name = bar }'
Console.WriteLine(msg2.Data);
```

## Dealing with Binary Data and Buffers

The default serializer can handle binary data and buffers. This is typically archived by using [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1)
implementations. NATS .NET Client provides a [`NatsMemoryOwner<T>`](xref:NATS.Client.Core.NatsMemoryOwner`1) implementation that can be used to allocate buffers.
The [`NatsMemoryOwner<T>`](xref:NATS.Client.Core.NatsMemoryOwner`1) and [`NatsBufferWriter<T>`](xref:NATS.Client.Core.NatsBufferWriter`1) (adapted from [.NET Community Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/high-performance/memoryowner))
are [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1) and [`IBufferWriter<T>`](https://learn.microsoft.com/dotnet/api/system.buffers.ibufferwriter-1) implementations that use the [`ArrayPool`](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1)
to allocate buffers. They can be used with the default serializer.

```csharp
// Same as not specifying a serializer.
var natsOpts = NatsOpts.Default with { Serializer = NatsDefaultSerializer.Default };

await using var nats = new NatsConnection(natsOpts);

await using var sub = await nats.SubscribeAsync<NatsMemoryOwner<byte>>(subject: "foo");

// Flush the the network buffers to make sure the subscription request has been processed.
await nats.PingAsync();

// Don't reuse NatsBufferWriter, it's disposed and returned to the pool
// by the publisher after being written to network.
var bw = new NatsBufferWriter<byte>();
var memory = bw.GetMemory(2);
memory.Span[0] = (byte)'H';
memory.Span[1] = (byte)'i';
bw.Advance(2);

await nats.PublishAsync(subject: "foo", data: bw);

var msg = await sub.Msgs.ReadAsync();

// Dispose the memory owner after using it so it can be retunrned to the pool.
using (var memoryOwner = msg.Data)
{
    // Outputs 'Hi'
    Console.WriteLine(Encoding.ASCII.GetString(memoryOwner.Memory.Span));
}
```

Advantage of using [`NatsMemoryOwner<T>`](xref:NATS.Client.Core.NatsMemoryOwner`1) and [`NatsBufferWriter<T>`](xref:NATS.Client.Core.NatsBufferWriter`1) is that they can be used with the default serializer and
they can be used to allocate buffers from the [`ArrayPool<T>`](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1) which can be reused. This is useful if you need to allocate
buffers for binary data and you want to avoid allocating buffers on for every operation (e.g. `new byte[]`) reducing
garbage collection pressure. They may also be useful for example, if your subscription may receive messages with
different formats and the only way to determine the format is by reading the message.
