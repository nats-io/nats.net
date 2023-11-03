# Serialization

NATS .NET Client supports serialization of messages using a simple interface [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1).

```csharp
public interface INatsSerializer<T>
{
    // Serialize the value to the buffer.
    void Serialize(IBufferWriter<byte> bufferWriter, T value);

    // Deserialize the value from the buffer.
    T? Deserialize(in ReadOnlySequence<byte> buffer);
}
```

By default, the client uses the [`NatsDefaultSerializer<T>`](xref:NATS.Client.Core.NatsDefaultSerializer`1) which can handle binary data, UTF8 strings and numbers. You can provide your own
serializer by implementing the [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1) interface or using the [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) for generated
JSON serialization. Serializers can also be chained together to provide multiple serialization formats typically
depending on the types being used.

## Default Serializer Registry

Default serializer is used when no serializer is provided to the connection options. It can handle binary data, UTF8
strings and numbers. It uses the following rules to determine the type of the data:

- If the data is a byte array, [`Memory<byte>`](https://learn.microsoft.com/dotnet/api/system.memory-1), [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1) or similar it is treated as binary data.
- If the data is a string or similar it is treated as UTF8 string.
- If the data is a primitive (for example `DateTime`, `int` or `double`. See also [`NatsUtf8PrimitivesSerializer<T>`](xref:NATS.Client.Core.NatsUtf8PrimitivesSerializer`1)) it is treated as the primitive encoded as a UTF8 string.
- For any other type, the serializer will throw an exception.

Serializer registry is a simple interface that can be used to provide a custom serializer instances for specific types:

```csharp
public interface INatsSerializerRegistry
{
    INatsSerializer<T> GetSerializer<T>();
}
```

You can use the default serializer by not specifying a serializer in the connection options or by setting the serializer
registry to the default serializer:

```csharp
// Same as not specifying a serializer.
var natsOpts = NatsOpts.Default with { Serializers = NatsDefaultSerializerRegistry.Default };

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
the [`NatsJsonSerializer<T>`](xref:NATS.Client.Serializers.Json.NatsJsonSerializer`1) to serialize and deserialize
messages. [`NatsJsonSerializer<T>`](xref:NATS.Client.Serializers.Json.NatsJsonSerializer`1) uses [`System.Text.Json`](https://learn.microsoft.com/dotnet/api/system.text.json)
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

var natsOpts = NatsOpts.Default with { Serializers = NatsJsonSerializerRegistry.Default };

await using var nats = new NatsConnection(natsOpts);
```

## Using JSON Serializer Context

The [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) uses the [`System.Text.Json`](https://learn.microsoft.com/dotnet/api/system.text.json) serializer to serialize and deserialize messages. It relies
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

Then you can use the [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) to serialize and deserialize messages
by providing the registry ([`NatsJsonContextSerializerRegistry`](xref:NATS.Client.Core.NatsJsonContextSerializerRegistry)) with the connection options:

```csharp
// Set the custom serializer registry as the default for the connection.
var myRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default, OtherJsonContext.Default);

var natsOpts = NatsOpts.Default with { Serializers = myRegistry };

await using var nats = new NatsConnection(natsOpts);

await using INatsSub<MyData> sub = await nats.SubscribeAsync<MyData>(subject: "foo");

// ...

await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" });

// ...
```

You can also set the serializer for a specific subscription or publish call:

```csharp
var myJson = new NatsJsonContextSerializer(MyJsonContext.Default);

await using INatsSub<MyData> sub = await nats.SubscribeAsync<MyData>(subject: "foo", serializer: myJson);

// ...

await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" }, serializer: myJson);

// ...
```

## Using Custom Serializer

You can also provide your own serializer by implementing the [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1) interface. This is useful if you need to
support a custom serialization format or if you need to support multiple serialization formats.

Here is an example of a custom serializer that uses the Google ProtoBuf serializer to serialize and deserialize:

```csharp
public class MyProtoBufSerializer<T> : INatsSerializer<T>
{
    public static readonly INatsSerializer Default = new MyProtoBufSerializer();

    public void Serialize(IBufferWriter<byte> bufferWriter, T value)
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

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) == typeof(Greeting))
        {
            return (T)(object)Greeting.Parser.ParseFrom(buffer);
        }

        throw new NatsException($"Can't deserialize {typeof(T)}");
    }
}

public class MyProtoBufSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerializer<T> GetSerializer<T>() => MyProtoBufSerializer<T>.Default;
}
```

You can then use the custom serializer as the default for the connection:

```csharp
var natsOpts = NatsOpts.Default with { Serializers = new MyProtoBufSerializerRegistry() };

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

Note that chaining serializers is implemented by convention and not enforced by the [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1)
interface since the next serializer would not be exposed to external users of the interface.

Here is an example of a serializer that uses the Google ProtoBuf serializer and the [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) to
serialize and deserialize messages based on the type:

```csharp
public class MixedSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerializer<T> GetSerializer<T>() => new NatsJsonContextSerializer<T>(MyJsonContext.Default, next: MyProtoBufSerializer<T>.Default);
}

var natsOpts = NatsOpts.Default with { Serializers =  new MixedSerializerRegistry() };

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
var natsOpts = NatsOpts.Default with { Serializer = NatsDefaultSerializerRegistry.Default };

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
