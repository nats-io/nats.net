using System.Buffers;
using System.Text;
using System.Text.Json.Serialization;
using Google.Protobuf;
using NATS.Client.Core;

// string
{
    // Same as not specifying a serializer.
    var natsOpts = NatsOpts.Default with { SerializerRegistry = NatsDefaultSerializerRegistry.Default };

    await using var nats = new NatsConnection(natsOpts);

    var sub = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<string>("foo"))
        {
            // Outputs 'Hello World'
            Console.WriteLine(msg.Data);
            break;
        }
    });

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync<string>(subject: "foo", data: "Hello World");

    await sub;
}

// custom JSON
{
    var natsOpts = NatsOpts.Default with { SerializerRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default) };

    await using var nats = new NatsConnection(natsOpts);

    var sub = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<MyData>("foo"))
        {
            // Outputs 'MyData { Id = 1, Name = bar }'
            Console.WriteLine(msg.Data);
            break;
        }
    });

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" });

    await sub;
}

// custom JSON
{
    await using var nats = new NatsConnection();

    var serializer = new NatsJsonContextSerializer<MyData>(MyJsonContext.Default);

    var sub = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<MyData>("foo"))
        {
            // Outputs 'MyData { Id = 1, Name = bar }'
            Console.WriteLine(msg.Data);
            break;
        }
    });

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" }, serializer: serializer);

    await sub;
}

// Protobuf
{
    var natsOpts = NatsOpts.Default with { SerializerRegistry = new MyProtoBufSerializerRegistry() };

    await using var nats = new NatsConnection(natsOpts);

    var sub = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<Greeting>("foo"))
        {
            // Outputs '{ "id": 42, "name": "Marvin" }'
            Console.WriteLine(msg.Data);
            break;
        }
    });

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync(subject: "foo", data: new Greeting { Id = 42, Name = "Marvin" });

    await sub;
}

// Protobuf/JSON
{
    var serializers = new MixedSerializerRegistry();
    var natsOpts = NatsOpts.Default with { SerializerRegistry = serializers };

    await using var nats = new NatsConnection(natsOpts);

    var sub1 = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<Greeting>("greet"))
        {
            // Outputs '{ "id": 42, "name": "Marvin" }'
            Console.WriteLine(msg.Data);
            break;
        }
    });

    var sub2 = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<MyData>("data"))
        {
            // Outputs 'MyData { Id = 1, Name = bar }'
            Console.WriteLine(msg.Data);
            break;
        }
    });

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync(subject: "greet", data: new Greeting { Id = 42, Name = "Marvin" });
    await nats.PublishAsync(subject: "data", data: new MyData { Id = 1, Name = "Bob" });

    await sub1;
    await sub2;
}

// Binary
{
    // Same as not specifying a serializer.
    var natsOpts = NatsOpts.Default with { SerializerRegistry = NatsDefaultSerializerRegistry.Default };

    await using var nats = new NatsConnection(natsOpts);

    var sub = Task.Run(async () =>
    {
        await foreach (var msg in nats.SubscribeAsync<NatsMemoryOwner<byte>>(subject: "foo"))
        {
            using (var memoryOwner = msg.Data)
            {
                // Outputs 'Hi'
                Console.WriteLine(Encoding.ASCII.GetString(memoryOwner.Memory.Span));
            }

            break;
        }
    });

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    var bw = new NatsBufferWriter<byte>();
    var memory = bw.GetMemory(2);
    memory.Span[0] = (byte)'H';
    memory.Span[1] = (byte)'i';
    bw.Advance(2);

    await nats.PublishAsync(subject: "foo", data: bw);

    await sub;
}

public class MixedSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerialize<T> GetSerializer<T>() => new NatsJsonContextSerializer<T>(MyJsonContext.Default, MyProtoBufSerializer<T>.Default);

    public INatsDeserialize<T> GetDeserializer<T>() => new NatsJsonContextSerializer<T>(MyJsonContext.Default, MyProtoBufSerializer<T>.Default);
}

public class MyProtoBufSerializerRegistry : INatsSerializerRegistry
{
    public INatsSerialize<T> GetSerializer<T>() => MyProtoBufSerializer<T>.Default;

    public INatsDeserialize<T> GetDeserializer<T>() => MyProtoBufSerializer<T>.Default;
}

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
}

public record MyData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

[JsonSerializable(typeof(MyData))]
internal partial class MyJsonContext : JsonSerializerContext;
