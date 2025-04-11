# Serialization

NATS.Net supports serialization of messages using a simple interface [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1).

By default, the client uses the [`NatsClientDefaultSerializer<T>`](xref:NATS.Net.NatsClientDefaultSerializer`1)
which can handle binary data, UTF8 strings, numbers, and ad hoc JSON serialization. You can provide your own
serializer by implementing the [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1) interface or using the
[`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) for generated
JSON serialization. Serializers can also be chained together to provide multiple serialization formats typically
depending on the types being used.

## Serializer Registries

There are two default serializer registries that can be used to provide serializers for specific types.
For any other serializers, you can implement your own serializer registry.

### NatsClientDefaultSerializer

This is the default serializer for [`NatsClient`](xref:NATS.Net.NatsClient) that
is used when no serializer registry is provided as an option.

- Can serialize what `NatsDefaultSerializerRegistry` can (see below).
- Additionally, it can serialize data classes using ad hoc JSON serialization.
- Uses reflection to generate serialization code at runtime so it's not AOT friendly.

The default client serializer is designed to be used by developers
who want to have an out-of-the-box experience for basic use cases like sending and receiving UTF8 strings,
or JSON messages.

### NatsDefaultSerializerRegistry

This is the default serializer for [`NatsConnection`](xref:NATS.Client.Core.NatsConnection) that
is used when no serializer registry is provided as an option.
See also [the differences between NatsClient vs NatsConnection](intro.md#natsclient-vs-natsconnection)

- AOT friendly
- If the data is a byte array, [`Memory<byte>`](https://learn.microsoft.com/dotnet/api/system.memory-1), [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1) or similar it is treated as binary data.
- If the data is a string or similar it is treated as UTF8 string.
- If the data is a primitive (for example `DateTime`, `int` or `double`. See also [`NatsUtf8PrimitivesSerializer<T>`](xref:NATS.Client.Core.NatsUtf8PrimitivesSerializer`1)) it is treated as the primitive encoded as a UTF8 string.
- For any other type, the serializer will throw an exception.

The default connection serializer is designed to be AOT friendly and mostly suitable for binary data.

### Using Custom Serializer Registries

Serialising custom data formats can be done by implementing the serializer registry interface
[`INatsSerializerRegistry`](xref:NATS.Client.Core.INatsSerializerRegistry)
that can be used to provide a custom serializer instances for specific types.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#default)]

## Using JSON Serializer Context

The [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1)
uses the [`System.Text.Json`](https://learn.microsoft.com/dotnet/api/system.text.json)
serializer to serialize and deserialize messages. It relies
on the [`System.Text.Json` source generator](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-source-generator/)
to generate the serialization code at compile time. This is the recommended JSON serializer for most use cases and it's
required for [Native AOT deployments](https://learn.microsoft.com/dotnet/core/deploying/native-aot).

First you need to define your JSON classes and a context to generate the serialization code:
[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#my-data)]

Then you can use the [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) to serialize and deserialize messages
by providing the registry ([`NatsJsonContextSerializerRegistry`](xref:NATS.Client.Core.NatsJsonContextSerializerRegistry)) with the connection options:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#my-data-usage)]

You can also set the serializer for a specific subscription or publish call:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#my-data-publish)]

## Using Custom Serializer

You can also provide your own serializer by implementing the [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1) interface. This is useful if you need to
support a custom serialization format or if you need to support multiple serialization formats.

Here is an example of a custom serializer that uses the Google ProtoBuf serializer to serialize and deserialize:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#custom-serializer)]

You can then use the custom serializer as the default for the connection:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#custom)]

## Using Multiple Serializers (chaining)

You can also chain multiple serializers together to support multiple serialization formats. The first serializer in the
chain that can handle the data will be used. This is useful if you need to support multiple serialization formats and
reuse them.

Note that chaining serializers is implemented by convention and not enforced by the [`INatsSerializer<T>`](xref:NATS.Client.Core.INatsSerializer`1)
interface since the next serializer would not be exposed to external users of the interface.

Here is an example of a serializer that uses the Google ProtoBuf serializer and the [`NatsJsonContextSerializer<T>`](xref:NATS.Client.Core.NatsJsonContextSerializer`1) to
serialize and deserialize messages based on the type:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#mixed)]

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#chain)]

## Dealing with Binary Data and Buffers

The default serializer can handle binary data and buffers. This is typically archived by using [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1)
implementations. NATS .NET Client provides a [`NatsMemoryOwner<T>`](xref:NATS.Client.Core.NatsMemoryOwner`1) implementation that can be used to allocate buffers.
The [`NatsMemoryOwner<T>`](xref:NATS.Client.Core.NatsMemoryOwner`1) and [`NatsBufferWriter<T>`](xref:NATS.Client.Core.NatsBufferWriter`1) (adapted from [.NET Community Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/high-performance/memoryowner))
are [`IMemoryOwner<byte>`](https://learn.microsoft.com/dotnet/api/system.buffers.imemoryowner-1) and [`IBufferWriter<T>`](https://learn.microsoft.com/dotnet/api/system.buffers.ibufferwriter-1) implementations that use the [`ArrayPool`](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1)
to allocate buffers. They can be used with the default serializer.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SerializationPage.cs#buffers)]

Advantage of using [`NatsMemoryOwner<T>`](xref:NATS.Client.Core.NatsMemoryOwner`1) and [`NatsBufferWriter<T>`](xref:NATS.Client.Core.NatsBufferWriter`1) is that they can be used with the default serializer and
they can be used to allocate buffers from the [`ArrayPool<T>`](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1) which can be reused. This is useful if you need to allocate
buffers for binary data and you want to avoid allocating buffers on for every operation (e.g. `new byte[]`) reducing
garbage collection pressure. They may also be useful for example, if your subscription may receive messages with
different formats and the only way to determine the format is by reading the message.
