using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.ObjectStore;

/*
 * Use nats-server > 2.10
 * .\client-compatibility.exe suite object-store default-bucket
 */

var url = Environment.GetEnvironmentVariable("NATS_URL") ?? NatsOpts.Default.Url;
var opts = NatsOpts.Default with { Url = url };
await using var nats = new NatsConnection(opts);
var js = new NatsJSContext(nats);
var ob = new NatsObjContext(js);

Log($"Connected to NATS server {url}");

await using var sub = await nats.SubscribeAsync<Memory<byte>>("tests.object-store.default-bucket.>");

Log($"Subscribed to {sub.Subject}");
var msg = await sub.Msgs.ReadAsync();

var config = JsonSerializer.Deserialize<ObjectStepConfig<BucketConfig>>(msg.Data.Span);

Log($"Test message received: {config}");

await ob.CreateObjectStore(new NatsObjConfig(config!.Config.Bucket!));

await msg.ReplyAsync<object>(default);

void Log(string message)
{
    Console.WriteLine($"{DateTime.Now:hh:mm:ss} {message}");
}

public record ObjectStepConfig<T>
{
    [JsonPropertyName("suite")]
    public string? Suite { get; set; }

    [JsonPropertyName("test")]
    public string? Test { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("config")]
    public T? Config { get; set; }
}

public record BucketConfig
{
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }
}
