// ReSharper disable AccessToDisposedClosure
// ReSharper disable RedundantTypeArgumentsOfMethod
// ReSharper disable ReturnTypeCanBeNotNullable
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnassignedGetOnlyAutoProperty
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable SA1515
#pragma warning disable SA1202

using System.Buffers;
using System.Text;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using NATS.Client.Core;

#region using-json
using NATS.Client.Serializers.Json;
#endregion

namespace NATS.Net.DocsExamples;

public class SerializationPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.SerializationPage");

        {
            Console.WriteLine("  #region default");

            #region default
            // Same as not specifying a serializer.
            var natsOpts = NatsOpts.Default with { SerializerRegistry = NatsDefaultSerializerRegistry.Default };

            await using var nats = new NatsConnection(natsOpts);

            var subscriber = Task.Run(async () =>
            {
                // Default serializer knows how to deal with UTF8 strings, numbers and binary data.
                await foreach (var msg in nats.SubscribeAsync<string>("foo"))
                {
                    // Check for the end of messages.
                    if (msg.Data == null)
                        break;

                    // Outputs 'Hello World'
                    Console.WriteLine(msg.Data);
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            // Default serializer knows how to deal with UTF8 strings, numbers and binary data.
            await nats.PublishAsync<string>(subject: "foo", data: "Hello World");

            // Signal the end of messages by sending an empty payload.
            await nats.PublishAsync(subject: "foo");

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region json");

            #region json
            var natsOpts = NatsOpts.Default with { SerializerRegistry = NatsJsonSerializerRegistry.Default };

            await using var nats = new NatsConnection(natsOpts);
            #endregion
        }

        {
            Console.WriteLine("  #region my-data-usage");

            #region my-data-usage
            // Set the custom serializer registry as the default for the connection.
            var myRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default, OtherJsonContext.Default);

            var natsOpts = NatsOpts.Default with { SerializerRegistry = myRegistry };

            await using var nats = new NatsConnection(natsOpts);

            var subscriber = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<MyData>("foo"))
                {
                    // Outputs 'MyData { Id = 1, Name = bar }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" });

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region my-data-publish");

            #region my-data-publish
            await using var nats = new NatsConnection();

            var serializer = new NatsJsonContextSerializer<MyData>(MyJsonContext.Default);

            var subscriber = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<MyData>("foo", serializer: serializer))
                {
                    // Outputs 'MyData { Id = 1, Name = bar }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" }, serializer: serializer);

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region custom");

            #region custom
            var natsOpts = NatsOpts.Default with { SerializerRegistry = new MyProtoBufSerializerRegistry() };

            await using var nats = new NatsConnection(natsOpts);

            var subscriber = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<Greeting>("foo"))
                {
                    // Outputs '{ "id": 42, "name": "Marvin" }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            await nats.PublishAsync(subject: "foo", data: new Greeting { Id = 42, Name = "Marvin" });

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region chain");

            #region chain
            var natsOpts = NatsOpts.Default with { SerializerRegistry = new MixedSerializerRegistry() };

            await using var nats = new NatsConnection(natsOpts);

            var subscriber1 = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<Greeting>("greet"))
                {
                    // Outputs '{ "id": 42, "name": "Marvin" }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            var subscriber2 = Task.Run(async () =>
            {
                await foreach (var msg in nats.SubscribeAsync<MyData>("data"))
                {
                    // Outputs 'MyData { Id = 1, Name = bar }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscribers a chance to connect.
            await Task.Delay(1000);

            await nats.PublishAsync(subject: "greet", data: new Greeting { Id = 42, Name = "Marvin" });
            await nats.PublishAsync(subject: "data", data: new MyData { Id = 1, Name = "Bob" });

            await Task.WhenAll(subscriber1, subscriber2);

            #endregion
        }

        {
            Console.WriteLine("  #region buffers");

            #region buffers
            // Same as not specifying a serializer.
            var natsOpts = NatsOpts.Default with { SerializerRegistry = NatsDefaultSerializerRegistry.Default };

            await using var nats = new NatsConnection(natsOpts);

            var subscriber = Task.Run(async () =>
            {
                // Default serializer knows how to deal with binary data types like NatsMemoryOwner<byte>.
                await foreach (var msg in nats.SubscribeAsync<NatsMemoryOwner<byte>>("foo"))
                {
                    // Check for the end of messages.
                    if (msg.Data.Length == 0)
                        break;

                    // Dispose the memory owner after using it so it can be returned to the pool.
                    using var memoryOwner = msg.Data;

                    // Outputs 'Hi'
                    Console.WriteLine(Encoding.ASCII.GetString(memoryOwner.Memory.Span));
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            // Don't reuse NatsBufferWriter, it's disposed and returned to the pool
            // by the publisher after being written to network.
            var bw = new NatsBufferWriter<byte>();
            var memory = bw.GetMemory(2);
            memory.Span[0] = (byte)'H';
            memory.Span[1] = (byte)'i';
            bw.Advance(2);

            // Default serializer knows how to deal with binary data types like NatsBufferWriter<byte>.
            await nats.PublishAsync(subject: "foo", data: bw);

            // Signal the end of messages by sending an empty payload.
            await nats.PublishAsync(subject: "foo");

            await subscriber;
            #endregion
        }
    }
}

#region my-data
public record MyData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

[JsonSerializable(typeof(MyData))]
internal partial class MyJsonContext : JsonSerializerContext
{
}
#endregion

public record MyOtherData;

[JsonSerializable(typeof(MyOtherData))]
internal partial class OtherJsonContext : JsonSerializerContext
{
}

#region custom-serializer
public class MyProtoBufSerializer<T> : INatsSerializer<T>
{
    public static readonly INatsSerializer<T> Default = new MyProtoBufSerializer<T>();

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

    public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => throw new NotImplementedException();
}

public class MyProtoBufSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerialize<T> GetSerializer<T>() => MyProtoBufSerializer<T>.Default;

    public INatsDeserialize<T> GetDeserializer<T>() => MyProtoBufSerializer<T>.Default;
}
#endregion

#region mixed
public class MixedSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerialize<T> GetSerializer<T>() => new NatsJsonContextSerializer<T>(MyJsonContext.Default, next: MyProtoBufSerializer<T>.Default);

    public INatsDeserialize<T> GetDeserializer<T>() => new NatsJsonContextSerializer<T>(MyJsonContext.Default, next: MyProtoBufSerializer<T>.Default);
}
#endregion

// Fake protobuf message.
// Normally this would be generated using protobuf compiler.
public class Greeting : IBufferMessage
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public MessageDescriptor Descriptor { get; }

    public void MergeFrom(CodedInputStream input)
    {
    }

    public void WriteTo(CodedOutputStream output)
    {
    }

    public int CalculateSize() => 1;

    public void InternalMergeFrom(ref ParseContext ctx)
    {
    }

    public void InternalWriteTo(ref WriteContext ctx)
    {
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    public class Parser
    {
        // ReSharper disable once UnusedParameter.Global
        public static Greeting ParseFrom(in ReadOnlySequence<byte> buffer) => new();
    }
}
