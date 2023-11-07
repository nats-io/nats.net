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

    await using var sub = await nats.SubscribeAsync<string>(subject: "foo");

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync<string>(subject: "foo", data: "Hello World");

    var msg = await sub.Msgs.ReadAsync();

    // Outputs 'Hello World'
    Console.WriteLine(msg.Data);
}

// custom JSON
{
    var natsOpts = NatsOpts.Default with { SerializerRegistry = new NatsJsonContextSerializerRegistry(MyJsonContext.Default) };

    await using var nats = new NatsConnection(natsOpts);

    await using var sub = await nats.SubscribeAsync<MyData>(subject: "foo");

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" });

    var msg = await sub.Msgs.ReadAsync();

    // Outputs 'MyData { Id = 1, Name = bar }'
    Console.WriteLine(msg.Data);
}

// custom JSON
{
    await using var nats = new NatsConnection();

    var serializer = new NatsJsonContextSerializer<MyData>(MyJsonContext.Default);

    await using var sub = await nats.SubscribeAsync<MyData>(subject: "foo", serializer: serializer);

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync<MyData>(subject: "foo", data: new MyData { Id = 1, Name = "bar" }, serializer: serializer);

    var msg = await sub.Msgs.ReadAsync();

    // Outputs 'MyData { Id = 1, Name = bar }'
    Console.WriteLine(msg.Data);
}

// Protobuf
{
    var natsOpts = NatsOpts.Default with { SerializerRegistry = new MyProtoBufSerializerRegistry() };

    await using var nats = new NatsConnection(natsOpts);

    await using var sub = await nats.SubscribeAsync<Greeting>(subject: "foo");

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    await nats.PublishAsync(subject: "foo", data: new Greeting { Id = 42, Name = "Marvin" });

    var msg = await sub.Msgs.ReadAsync();

    // Outputs '{ "id": 42, "name": "Marvin" }'
    Console.WriteLine(msg.Data);
}

// Protobuf/JSON
{
    var serializers = new MixedSerializerRegistry();
    var natsOpts = NatsOpts.Default with { SerializerRegistry = serializers };

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
}

// Binary
{
    // Same as not specifying a serializer.
    var natsOpts = NatsOpts.Default with { SerializerRegistry = NatsDefaultSerializerRegistry.Default };

    await using var nats = new NatsConnection(natsOpts);

    await using var sub = await nats.SubscribeAsync<NatsMemoryOwner<byte>>(subject: "foo");

    // Flush the the network buffers to make sure the subscription request has been processed.
    await nats.PingAsync();

    var bw = new NatsBufferWriter<byte>();
    var memory = bw.GetMemory(2);
    memory.Span[0] = (byte)'H';
    memory.Span[1] = (byte)'i';
    bw.Advance(2);

    await nats.PublishAsync(subject: "foo", data: bw);

    var msg = await sub.Msgs.ReadAsync();

    using (var memoryOwner = msg.Data)
    {
        // Outputs 'Hi'
        Console.WriteLine(Encoding.ASCII.GetString(memoryOwner.Memory.Span));
    }
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
