using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using NATS.Client.Core.Tests;
using NATS.Client.ObjectStore.Internal;
using NATS.Client.ObjectStore.Models;

namespace NATS.Client.ObjectStore.Tests;

public class ObjectStoreTest
{
    private readonly ITestOutputHelper _output;

    public ObjectStoreTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_store()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10000));
        var cancellationToken = cts.Token;

        // await using var server = NatsServer.StartJS();
        // await using var nats = server.CreateClientConnection();
        var nats = new NatsConnection();

        var js = new NatsJSContext(nats);
        var ob = new NatsOBContext(js);

        var store = await ob.CreateObjectStore(new NatsOBConfig("b1"), cancellationToken);

        var stringBuilder = new StringBuilder();
        for (var i = 0; i < 9; i++)
        {
            stringBuilder.Append($"{i:D2}-4567890");
        }

        var buffer90 = stringBuilder.ToString();

        // square buffer: all chunks are the same size
        {
            var meta = new ObjectMetadata { Name = "k1", Options = new Options { MaxChunkSize = 10 }, };

            var buffer = Encoding.ASCII.GetBytes(buffer90);
            var stream = new MemoryStream(buffer);

            await store.PutAsync(meta, stream, cancellationToken);

            var data = await store.GetInfoAsync("k1", cancellationToken);

            _output.WriteLine($"MSG.DATA:{data}");
            _output.WriteLine($"CHUNKS={Math.Ceiling(buffer.Length / 10.0)}");
            _output.WriteLine($"SIZE={buffer.Length}");
            _output.WriteLine($"sha:{Base64UrlEncoder.Encode(SHA256.HashData(buffer))}");
        }

        // buffer with smaller last chunk
        {
            var meta = new ObjectMetadata { Name = "k2", Options = new Options { MaxChunkSize = 10 }, };

            var buffer = Encoding.ASCII.GetBytes(buffer90 + "09-45");
            var stream = new MemoryStream(buffer);

            await store.PutAsync(meta, stream, cancellationToken);

            var data = await store.GetInfoAsync("k2", cancellationToken);

            _output.WriteLine($"MSG.DATA:{data}");
            _output.WriteLine($"CHUNKS={Math.Ceiling(buffer.Length / 10.0)}");
            _output.WriteLine($"SIZE={buffer.Length}");
            _output.WriteLine($"SHA-256={Base64UrlEncoder.Encode(SHA256.HashData(buffer))}");
        }
    }
}
