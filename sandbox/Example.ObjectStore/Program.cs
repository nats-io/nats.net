using System.Security.Cryptography;
using Example.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.ObjectStore;

using var tracer = TracingSetup.RunSandboxTracing();

var opts = NatsOpts.Default with { LoggerFactory = LoggerFactory.Create(builder => builder.AddConsole()) };

var nats = new NatsConnection(opts);
var js = new NatsJSContext(nats);
var obj = new NatsObjContext(js);

Log("Create object store...");
var store = await obj.CreateObjectStoreAsync("test-bucket");

var data = new byte[1024 * 1024 * 10];
Random.Shared.NextBytes(data);

File.WriteAllBytes("data.bin", data);

Log("Put file...");
await store.PutAsync("my/random/data.bin", File.OpenRead("data.bin"));

Log("Get file...");
await store.GetAsync("my/random/data.bin", File.OpenWrite("data1.bin"));

var hash = Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes("data.bin")));
var hash1 = Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes("data1.bin")));

Log($"Check SHA-256: {hash == hash1}");

var metadata = await store.GetInfoAsync("my/random/data.bin");

Console.WriteLine("Metadata:");
Console.WriteLine($"  Bucket: {metadata.Bucket}");
Console.WriteLine($"  Name: {metadata.Name}");
Console.WriteLine($"  Size: {metadata.Size}");
Console.WriteLine($"  Time: {metadata.MTime}");
Console.WriteLine($"  Chunks: {metadata.Chunks}");

await store.DeleteAsync("my/random/data.bin");

Console.WriteLine("Bye");

void Log(string message)
{
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
}
