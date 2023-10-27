using System.Text.Json.Serialization;
using NATS.Client.Core;

await using var nats = new NatsConnection();

await using var sub = await nats.SubscribeAsync<MyData>("foo", opts: new NatsSubOpts { Serializer = new NatsJsonContextSerializer(MyJsonContext.Default) });

await nats.PingAsync();

await nats.PublishAsync(
    "foo",
    new MyData
    {
        Id = 1, Name = "bar",
    },
    opts: new NatsPubOpts
    {
        Serializer = new NatsJsonContextSerializer(MyJsonContext.Default),
    });

var msg = await sub.Msgs.ReadAsync();

Console.WriteLine(msg.Data);

public record MyData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

[JsonSerializable(typeof(MyData))]
internal partial class MyJsonContext : JsonSerializerContext;
