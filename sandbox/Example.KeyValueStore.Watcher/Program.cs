using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

using var tracer = TracingSetup.RunSandboxTracing();

// var options = NatsOpts.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };
// var nats = new NatsConnection(options);
var nats = new NatsConnection();

var js = new NatsJSContext(nats);
var kv = new NatsKVContext(js);

var store = await kv.CreateStoreAsync("e1");

await foreach (var entry in store.WatchAsync<int>())
{
    Console.WriteLine($"[RCV] {entry}");
}
