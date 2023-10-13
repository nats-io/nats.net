using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.ObjectStore;
using NATS.Client.ObjectStore.Models;

var nats = new NatsConnection();
var js = new NatsJSContext(nats);
var ob = new NatsOBContext(js);

var store = await ob.CreateObjectStore(new NatsOBConfig("o1"));

var meta = new ObjectMetadata { Name = "k1", Options = new Options { MaxChunkSize = 10 }, };

var stringBuilder = new StringBuilder();
for (var i = 0; i < 9; i++)
{
    stringBuilder.Append($"{i:D2}-4567890");
}

var buffer90 = stringBuilder.ToString();
{
    var buffer = Encoding.ASCII.GetBytes(buffer90);
    var stream = new MemoryStream(buffer);

    await store.PutAsync(meta, stream);

    var data = await store.GetInfoAsync("k1");

    Console.WriteLine($"DATA: {data}");
}

{
    var memoryStream = new MemoryStream();
    await store.GetAsync("k1", memoryStream);
    await memoryStream.FlushAsync();
    var buffer = memoryStream.ToArray();
    Console.WriteLine($"buffer:{Encoding.ASCII.GetString(buffer)}");
}

Console.WriteLine("Bye");
