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

namespace NATS.Net.DocsExamples.Advanced;

public class SerializationPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.SerializationPage");

        {
            Console.WriteLine("  #region default");

            #region default
            // Set your custom serializer registry as the default for the connection.
            NatsOpts opts = NatsOpts.Default with { SerializerRegistry = new MyProtoBufSerializerRegistry() };

            await using NatsClient nc = new NatsClient(opts);
            #endregion
        }

        {
            Console.WriteLine("  #region my-data-usage");

            #region my-data-usage
            // Set the custom serializer registry as the default for the connection.
            NatsJsonContextSerializerRegistry myRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default, OtherJsonContext.Default);

            NatsOpts opts = new NatsOpts { SerializerRegistry = myRegistry };

            await using NatsClient nc = new NatsClient(opts);

            Task subscriber = Task.Run(async () =>
            {
                await foreach (NatsMsg<MyData> msg in nc.SubscribeAsync<MyData>("foo"))
                {
                    // Outputs 'MyData { Id = 1, Name = bar }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            await nc.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" });

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region my-data-publish");

            #region my-data-publish
            await using NatsClient nc = new NatsClient();

            NatsJsonContextSerializer<MyData> serializer = new NatsJsonContextSerializer<MyData>(MyJsonContext.Default);

            Task subscriber = Task.Run(async () =>
            {
                await foreach (NatsMsg<MyData> msg in nc.SubscribeAsync<MyData>("foo", serializer: serializer))
                {
                    // Outputs 'MyData { Id = 1, Name = bar }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            await nc.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" }, serializer: serializer);

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region custom");

            #region custom
            NatsOpts opts = new NatsOpts { SerializerRegistry = new MyProtoBufSerializerRegistry() };

            await using NatsClient nc = new NatsClient(opts);

            Task subscriber = Task.Run(async () =>
            {
                await foreach (NatsMsg<Greeting> msg in nc.SubscribeAsync<Greeting>("foo"))
                {
                    // Outputs '{ "id": 42, "name": "Marvin" }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            await nc.PublishAsync(subject: "foo", data: new Greeting { Id = 42, Name = "Marvin" });

            await subscriber;
            #endregion
        }

        {
            Console.WriteLine("  #region chain");

            #region chain
            NatsOpts opts = new NatsOpts { SerializerRegistry = new MixedSerializerRegistry() };

            await using NatsClient nc = new NatsClient(opts);

            Task subscriber1 = Task.Run(async () =>
            {
                await foreach (NatsMsg<Greeting> msg in nc.SubscribeAsync<Greeting>("greet"))
                {
                    // Outputs '{ "id": 42, "name": "Marvin" }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            Task subscriber2 = Task.Run(async () =>
            {
                await foreach (NatsMsg<MyData> msg in nc.SubscribeAsync<MyData>("data"))
                {
                    // Outputs 'MyData { Id = 1, Name = bar }'
                    Console.WriteLine(msg.Data);
                    break;
                }
            });

            // Give subscribers a chance to connect.
            await Task.Delay(1000);

            await nc.PublishAsync(subject: "greet", data: new Greeting { Id = 42, Name = "Marvin" });
            await nc.PublishAsync(subject: "data", data: new MyData { Id = 1, Name = "Bob" });

            await Task.WhenAll(subscriber1, subscriber2);

            #endregion
        }

        {
            Console.WriteLine("  #region buffers");

            #region buffers
            // The default serializer knows how to deal with binary data types like NatsMemoryOwner<byte>.
            // So, you can use it without specifying a serializer.
            await using NatsClient nc = new NatsClient();

            Task subscriber = Task.Run(async () =>
            {
                // The default serializer knows how to deal with binary data types like NatsMemoryOwner<byte>.
                await foreach (NatsMsg<NatsMemoryOwner<byte>> msg in nc.SubscribeAsync<NatsMemoryOwner<byte>>("foo"))
                {
                    // Check for the end of messages.
                    if (msg.Data.Length == 0)
                        break;

                    // Dispose the memory owner after using it so it can be returned to the pool.
                    using NatsMemoryOwner<byte> memoryOwner = msg.Data;

                    // Outputs 'Hi'
                    Console.WriteLine(Encoding.ASCII.GetString(memoryOwner.Memory.Span));
                }
            });

            // Give subscriber a chance to connect.
            await Task.Delay(1000);

            // Don't reuse NatsBufferWriter, it's disposed and returned to the pool
            // by the publisher after being written to the network.
            NatsBufferWriter<byte> bw = new NatsBufferWriter<byte>();
            Memory<byte> memory = bw.GetMemory(2);
            memory.Span[0] = (byte)'H';
            memory.Span[1] = (byte)'i';
            bw.Advance(2);

            // Default serializer knows how to deal with binary data types like NatsBufferWriter<byte>.
            await nc.PublishAsync(subject: "foo", data: bw);

            // Signal the end of messages by sending an empty payload.
            await nc.PublishAsync(subject: "foo");

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
// Normally, this would be generated using protobuf compiler.
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
