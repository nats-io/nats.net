using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

var subject = "bar.*";
var options = NatsOpts.Default with
{
    LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()),
    SerializerRegistry = new NatsJsonSerializerRegistry(),
};

Print("[CON] Connecting...\n");

await using var connection = new NatsConnection(options);

Print($"[SUB] Subscribing to subject '{subject}'...\n");

await foreach (var msg in connection.SubscribeAsync<Bar>(subject))
{
    Print($"[RCV] {msg.Subject}: {msg.Data}\n");
}

void Print(string message)
{
    Console.Write($"{DateTime.Now:HH:mm:ss} {message}");
}

public record Bar
{
    public int Id { get; set; }

    public string? Name { get; set; }
}
