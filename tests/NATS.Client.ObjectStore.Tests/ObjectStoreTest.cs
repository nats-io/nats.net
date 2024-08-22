using System.Security.Cryptography;
using System.Text;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.ObjectStore.Internal;
using NATS.Client.ObjectStore.Models;
using NATS.Client.Serializers.Json;

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

        await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

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

        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

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

        // Object name checks
        {
            var anyNameIsFine = "any name is fine '~#!$()*/\\,.?<>|{}[]`'\"";
            await store.PutAsync(anyNameIsFine, new byte[] { 42 }, cancellationToken: cancellationToken);
            var value = await store.GetBytesAsync(anyNameIsFine, cancellationToken);
            Assert.Single(value);
            Assert.Equal(42, value[0]);

            // can't be empty
            {
                var exception = await Assert.ThrowsAsync<NatsObjException>(async () => await store.PutAsync(string.Empty, new byte[] { 42 }, cancellationToken: cancellationToken));
                Assert.Matches("Object name can't be empty", exception.Message);
            }

            {
                var exception = await Assert.ThrowsAsync<NatsObjException>(async () => await store.PutAsync(null!, new byte[] { 42 }, cancellationToken: cancellationToken));
                Assert.Matches("Object name can't be empty", exception.Message);
            }
        }
    }

    [Fact]
    public async Task Get_chunks()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

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

        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);
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

        var store = await obj.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

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

    [Fact]
    public async Task Add_link()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var store1 = await obj.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);
        var store2 = await obj.CreateObjectStoreAsync(new NatsObjConfig("b2"), cancellationToken);

        await store1.PutAsync("k1", new byte[] { 42 }, cancellationToken: cancellationToken);

        // Link
        {
            await store1.AddLinkAsync(link: "link1", target: "k1", cancellationToken: cancellationToken);

            var info = await store1.GetInfoAsync("link1", cancellationToken: cancellationToken);
            Assert.Equal("k1", info.Options?.Link?.Name);
            Assert.Equal("b1", info.Options?.Link?.Bucket);
            Assert.Equal("link1", info.Name);
            Assert.Equal("b1", info.Bucket);

            var bytes = await store1.GetBytesAsync("link1", cancellationToken: cancellationToken);
            Assert.Single(bytes);
            Assert.Equal(42, bytes[0]);
        }

        // Bucket Link
        {
            await store2.AddBucketLinkAsync(link: "k1", store1, cancellationToken: cancellationToken);

            var info = await store2.GetInfoAsync("k1", cancellationToken: cancellationToken);
            Assert.Equal("k1", info.Options?.Link?.Name);
            Assert.Equal("b1", info.Options?.Link?.Bucket);
            Assert.Equal("k1", info.Name);
            Assert.Equal("b2", info.Bucket);

            var bytes = await store2.GetBytesAsync("k1", cancellationToken: cancellationToken);
            Assert.Single(bytes);
            Assert.Equal(42, bytes[0]);
        }
    }

    [Fact]
    public async Task Seal_and_get_status()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var store = await obj.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        await store.PutAsync("k1", new byte[] { 42 }, cancellationToken: cancellationToken);

        // Status
        {
            var status = await store.GetStatusAsync(cancellationToken);
            Assert.Equal(store.Bucket, status.Bucket);
            Assert.False(status.Info.Config.Sealed);
        }

        await store.SealAsync(cancellationToken);

        // Updated status
        {
            var status = await store.GetStatusAsync(cancellationToken);
            Assert.Equal(store.Bucket, status.Bucket);
            Assert.True(status.Info.Config.Sealed);
        }

        var exception = await Assert.ThrowsAsync<NatsJSApiException>(async () =>
            await store.PutAsync("k2", new byte[] { 13 }, cancellationToken: cancellationToken));

        Assert.Equal(400, exception.Error.Code);
        Assert.Equal("invalid operation on sealed stream", exception.Error.Description);
        Assert.Equal(10109, exception.Error.ErrCode);
    }

    [Fact]
    public async Task List()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var store = await obj.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        await store.PutAsync("k1", new byte[] { 42 }, cancellationToken: cancellationToken);
        await store.PutAsync("k2", new byte[] { 43 }, cancellationToken: cancellationToken);
        await store.PutAsync("k3", new byte[] { 44 }, cancellationToken: cancellationToken);
        await store.PutAsync("k4", new byte[] { 13 }, cancellationToken: cancellationToken);
        await store.DeleteAsync("k4", cancellationToken: cancellationToken);

        // List
        {
            var infos = new List<ObjectMetadata>();
            await foreach (var info in store.ListAsync(cancellationToken: cancellationToken))
            {
                infos.Add(info);
            }

            Assert.Equal(3, infos.Count);
            Assert.Equal("k1", infos[0].Name);
            Assert.Equal("k2", infos[1].Name);
            Assert.Equal("k3", infos[2].Name);
            Assert.False(infos[0].Deleted);
            Assert.False(infos[1].Deleted);
            Assert.False(infos[2].Deleted);
        }

        // List show deleted
        {
            var infos = new List<ObjectMetadata>();
            var opts = new NatsObjListOpts { ShowDeleted = true };
            await foreach (var info in store.ListAsync(opts, cancellationToken: cancellationToken))
            {
                infos.Add(info);
            }

            Assert.Equal(4, infos.Count);
            Assert.Equal("k1", infos[0].Name);
            Assert.Equal("k2", infos[1].Name);
            Assert.Equal("k3", infos[2].Name);
            Assert.Equal("k4", infos[3].Name);
            Assert.False(infos[0].Deleted);
            Assert.False(infos[1].Deleted);
            Assert.False(infos[2].Deleted);
            Assert.True(infos[3].Deleted);
        }
    }

    [Fact]
    public async Task List_empty_store_for_end_of_data()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var store = await obj.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        var signal = new WaitSignal(timeout);
        var endOfDataHit = false;
        var watchTask = Task.Run(
            async () =>
            {
                var opts = new NatsObjListOpts()
                {
                    OnNoData = async (_) =>
                    {
                        await Task.CompletedTask;
                        endOfDataHit = true;
                        signal.Pulse();
                        return true;
                    },
                };

                await foreach (var info in store.ListAsync(opts: opts, cancellationToken: cancellationToken))
                {
                }
            },
            cancellationToken);

        await signal;

        Assert.True(endOfDataHit, "End of Current Data not set");

        await watchTask;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.10")]
    public async Task Compressed_storage()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var obj = new NatsObjContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var store1 = await obj.CreateObjectStoreAsync(new NatsObjConfig("b1") { Compression = false }, cancellationToken);
        var store2 = await obj.CreateObjectStoreAsync(new NatsObjConfig("b2") { Compression = true }, cancellationToken);

        Assert.Equal("b1", store1.Bucket);
        Assert.Equal("b2", store2.Bucket);

        var status1 = await store1.GetStatusAsync(cancellationToken);
        Assert.Equal("b1", status1.Bucket);
        Assert.Equal("OBJ_b1", status1.Info.Config.Name);
        Assert.Equal(StreamConfigCompression.None, status1.Info.Config.Compression);

        var status2 = await store2.GetStatusAsync(cancellationToken);
        Assert.Equal("b2", status2.Bucket);
        Assert.Equal("OBJ_b2", status2.Info.Config.Name);
        Assert.Equal(StreamConfigCompression.S2, status2.Info.Config.Compression);
    }

    [Fact]
    public async Task Put_get_serialization_when_default_serializer_is_not_used()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection(options: new NatsOpts
        {
            SerializerRegistry = NatsJsonSerializerRegistry.Default,
        });
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        await store.PutAsync("k1", new byte[] { 42 }, cancellationToken: cancellationToken);

        var bytes = await store.GetBytesAsync("k1", cancellationToken);
        Assert.Equal(new byte[] { 42 }, bytes);

        var info = await store.GetInfoAsync("k1", cancellationToken: cancellationToken);
        Assert.Equal("k1", info.Name);
    }
}
