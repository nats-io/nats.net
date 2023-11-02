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
    public async Task Create_delete_object_store()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        await ob.CreateObjectStore(new NatsObjConfig("b1"), cancellationToken);

        await foreach (var stream in js.ListStreamsAsync(cancellationToken: cancellationToken))
        {
            Assert.Equal($"OBJ_b1", stream.Info.Config.Name);
        }

        var deleted = await ob.DeleteObjectStore("b1", cancellationToken);
        Assert.True(deleted);

        await foreach (var stream in js.ListStreamsAsync(cancellationToken: cancellationToken))
        {
            Assert.Fail("Should not have any streams");
        }
    }

    [Fact]
    public async Task Put_chunks()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var store = await ob.CreateObjectStore(new NatsObjConfig("b1"), cancellationToken);

        var stringBuilder = new StringBuilder();
        for (var i = 0; i < 9; i++)
        {
            stringBuilder.Append($"{i:D2}-4567890");
        }

        var buffer90 = stringBuilder.ToString();

        // square buffer: all chunks are the same size
        {
            var meta = new ObjectMetadata { Name = "k1", Options = new MetaDataOptions { MaxChunkSize = 10 }, };

            var buffer = Encoding.ASCII.GetBytes(buffer90);
            var stream = new MemoryStream(buffer);

            await store.PutAsync(meta, stream, cancellationToken: cancellationToken);

            var data = await store.GetInfoAsync("k1", cancellationToken: cancellationToken);

            var sha = Base64UrlEncoder.Encode(SHA256.HashData(buffer));
            var size = buffer.Length;
            var chunks = Math.Ceiling(size / 10.0);

            Assert.Equal($"SHA-256={sha}", data.Digest);
            Assert.Equal(chunks, data.Chunks);
            Assert.Equal(size, data.Size);
        }

        // buffer with smaller last chunk
        {
            var meta = new ObjectMetadata { Name = "k2", Options = new MetaDataOptions { MaxChunkSize = 10 }, };

            var buffer = Encoding.ASCII.GetBytes(buffer90 + "09-45");
            var stream = new MemoryStream(buffer);

            await store.PutAsync(meta, stream, cancellationToken: cancellationToken);

            var data = await store.GetInfoAsync("k2", cancellationToken: cancellationToken);

            var sha = Base64UrlEncoder.Encode(SHA256.HashData(buffer));
            var size = buffer.Length;
            var chunks = Math.Ceiling(size / 10.0);

            Assert.Equal($"SHA-256={sha}", data.Digest);
            Assert.Equal(chunks, data.Chunks);
            Assert.Equal(size, data.Size);
        }
    }

    [Fact]
    public async Task Get_chunks()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10000));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var store = await ob.CreateObjectStore(new NatsObjConfig("b1"), cancellationToken);

        var stringBuilder = new StringBuilder();
        for (var i = 0; i < 9; i++)
        {
            stringBuilder.Append($"{i:D2}-4567890");
        }

        var buffer90 = stringBuilder.ToString();

        // square buffer: all chunks are the same size
        {
            var meta = new ObjectMetadata { Name = "k1", Options = new MetaDataOptions { MaxChunkSize = 10 }, };
            var buffer = Encoding.ASCII.GetBytes(buffer90);
            var stream = new MemoryStream(buffer);
            await store.PutAsync(meta, stream, cancellationToken: cancellationToken);
        }

        {
            var memoryStream = new MemoryStream();
            await store.GetAsync("k1", memoryStream, cancellationToken: cancellationToken);
            await memoryStream.FlushAsync(cancellationToken);
            var buffer = memoryStream.ToArray();
            Assert.Equal(buffer90, Encoding.ASCII.GetString(buffer));
        }

        // buffer with smaller last chunk
        {
            var meta = new ObjectMetadata { Name = "k2", Options = new MetaDataOptions { MaxChunkSize = 10 }, };
            var buffer = Encoding.ASCII.GetBytes(buffer90 + "09-45");
            var stream = new MemoryStream(buffer);
            await store.PutAsync(meta, stream, cancellationToken: cancellationToken);
        }

        {
            var memoryStream = new MemoryStream();
            await store.GetAsync("k2", memoryStream, cancellationToken: cancellationToken);
            await memoryStream.FlushAsync(cancellationToken);
            var buffer = memoryStream.ToArray();
            Assert.Equal(buffer90 + "09-45", Encoding.ASCII.GetString(buffer));
        }
    }

    [Fact]
    public async Task Delete_object()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10_000));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var store = await ob.CreateObjectStore(new NatsObjConfig("b1"), cancellationToken);
        await store.PutAsync("k1", new byte[] { 65, 66, 67 }, cancellationToken);

        var info = await store.GetInfoAsync("k1", cancellationToken: cancellationToken);
        Assert.Equal(3, info.Size);

        var bytes = await store.GetBytesAsync("k1", cancellationToken);
        Assert.Equal(bytes, new byte[] { 65, 66, 67 });

        await store.DeleteAsync("k1", cancellationToken);

        var exception = await Assert.ThrowsAsync<NatsObjNotFoundException>(async () => await store.GetInfoAsync("k1", cancellationToken: cancellationToken));
        Assert.Matches("Object not found", exception.Message);

        var info2 = await store.GetInfoAsync("k1", showDeleted: true, cancellationToken: cancellationToken);
        Assert.True(info2.Deleted);
        Assert.Equal(0, info2.Size);
        Assert.Equal(0, info2.Chunks);
        Assert.Equal(string.Empty, info2.Digest);

        // Put again
        await store.PutAsync("k1", new byte[] { 65, 66, 67 }, cancellationToken);

        var info3 = await store.GetInfoAsync("k1", showDeleted: true, cancellationToken: cancellationToken);
        Assert.False(info3.Deleted);
    }

    [Fact]
    public async Task Put_and_get_large_file()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var store = await obj.CreateObjectStore(new NatsObjConfig("b1"), cancellationToken);

        var data = new byte[1024 * 1024 * 10];
        Random.Shared.NextBytes(data);

        const string filename = $"_tmp_test_file_{nameof(Put_and_get_large_file)}.bin";
        var filename1 = $"{filename}.1";

        await File.WriteAllBytesAsync(filename, data, cancellationToken);

        await store.PutAsync("my/random/data.bin", File.OpenRead(filename), cancellationToken: cancellationToken);

        await store.GetAsync("my/random/data.bin", File.OpenWrite(filename1), cancellationToken: cancellationToken);

        var hash = Convert.ToBase64String(SHA256.HashData(await File.ReadAllBytesAsync(filename, cancellationToken)));
        var hash1 = Convert.ToBase64String(SHA256.HashData(await File.ReadAllBytesAsync(filename1, cancellationToken)));

        Assert.Equal(hash, hash1);
    }
}
