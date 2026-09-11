#if NETFRAMEWORK
using System.Buffers;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.Versioning;
#endif
using System.Runtime.InteropServices;
using NATS.Client.ObjectStore.Internal;
using NATS.Client.ObjectStore.Models;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.ObjectStore.Tests;

// Covers the netstandard2.0-specific read loop in NatsObjStore.PutAsync. On net481 the resolved
// NATS.Client.ObjectStore is the netstandard2.0 build, which is the only one that compiles it;
// the net8.0 leg runs whatever is framework-agnostic here as a cross-check.
public class NetStandardPutPathTest
{
    private const int ChunkSize = 8 * 1024;

    private readonly ITestOutputHelper _output;

    public NetStandardPutPathTest(ITestOutputHelper output) => _output = output;

    // PutAsync reads into NatsMemoryOwner<byte>.Memory, which is always array-backed, so the
    // TryGetArray==true branch is the one that runs and the pooled-copy fallback is dead code.
    // If this ever fails the fallback has come alive and needs testing in its own right.
    [Fact]
    public void Chunk_buffer_is_array_backed()
    {
        using var owner = NatsMemoryOwner<byte>.Allocate(ChunkSize);

        Assert.True(MemoryMarshal.TryGetArray<byte>(owner.Memory, out var segment));
        Assert.NotNull(segment.Array);
        Assert.Equal(ChunkSize, segment.Count);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(ChunkSize - 1, false)]
    [InlineData(ChunkSize - 1, true)]
    [InlineData(ChunkSize, false)]
    [InlineData(ChunkSize, true)]
    [InlineData(ChunkSize + 1, false)]
    [InlineData(ChunkSize + 1, true)]
    [InlineData((ChunkSize * 5) / 2, false)]
    [InlineData((ChunkSize * 5) / 2, true)]
    [InlineData(ChunkSize * 4, false)]
    [InlineData(ChunkSize * 4, true)]
    public async Task Put_and_get_round_trip_across_chunk_boundaries(int size, bool trickle)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);
        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        var data = new byte[size];
        RandomCompat.Shared.NextBytes(data);

        // The trickling stream returns a few bytes per read, which drives the inner chunk-fill
        // loop where the direct-read path has to respect the advancing segment offset.
        using var stream = trickle ? (Stream)new TrickleStream(data) : new MemoryStream(data);
        var meta = new ObjectMetadata
        {
            Name = "obj",
            Options = new MetaDataOptions { MaxChunkSize = ChunkSize },
        };

        var put = await store.PutAsync(meta, stream, cancellationToken: cancellationToken);

        Assert.Equal((ulong)size, put.Size);
        Assert.Equal((uint)((size + ChunkSize - 1) / ChunkSize), put.Chunks);
        Assert.Equal("SHA-256=" + Base64UrlEncoder.Encode(Sha256Compat.HashData(data)), put.Digest);

        var got = await store.GetBytesAsync("obj", cancellationToken);
        Assert.Equal(data, got);
    }

#if NETFRAMEWORK
    // Everything below rests on net481 resolving the netstandard2.0 build of the client, since
    // that is the only one compiling the path under test. If the client ever gains a .NET
    // Framework target this leg would quietly stop covering it, so assert it outright.
    [Fact]
    public void Object_store_assembly_is_the_netstandard_build()
    {
        var tfm = typeof(NatsObjStore).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        _output.WriteLine($"NATS.Client.ObjectStore TFM: {tfm}");
        Assert.Equal(".NETStandard,Version=v2.0", tfm);
    }

    // Regression test for the inverted TryGetArray check: the broken code rented an
    // ArrayPool<byte>.Shared transfer buffer and copied it into the chunk buffer for every chunk
    // read, while the fixed code reads straight into the chunk buffer and never rents in the read
    // loop. Both variants produce identical objects, so renting is the only observable difference
    // and it is counted here through the pool's own ArrayPoolEventSource.
    //
    // The publish path rents about one chunk-sized buffer per published message either way, a
    // fixed background cost, so the two regimes are ~2 rents per chunk broken (measured 203 for
    // 100 chunks) versus ~1 per chunk fixed (measured 102); the threshold sits between them.
    [Fact]
    public async Task Put_does_not_rent_a_transfer_buffer_per_chunk()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        using var listener = new ArrayPoolRentListener(ChunkSize);

        // Without this the assertion below could pass vacuously by observing no events at all.
        var probe = ArrayPool<byte>.Shared.Rent(ChunkSize);
        ArrayPool<byte>.Shared.Return(probe);
        Assert.True(listener.Count > 0, "ArrayPoolEventSource is not observable in this process");

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();
        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);
        var store = await ob.CreateObjectStoreAsync(new NatsObjConfig("b1"), cancellationToken);

        const int chunks = 100;
        var data = new byte[ChunkSize * chunks];
        RandomCompat.Shared.NextBytes(data);

        var meta = new ObjectMetadata
        {
            Name = "obj",
            Options = new MetaDataOptions { MaxChunkSize = ChunkSize },
        };

        var before = listener.Count;
        using var stream = new MemoryStream(data);
        var put = await store.PutAsync(meta, stream, cancellationToken: cancellationToken);
        var rented = listener.Count - before;

        Assert.Equal((uint)chunks, put.Chunks);
        _output.WriteLine($"chunk-sized buffers rented during {chunks}-chunk put: {rented}");

        Assert.True(
            rented < chunks * 3 / 2,
            $"PutAsync rented {rented} chunk-sized buffers for {chunks} chunks; expected about one per chunk from the publish path alone. The netstandard2.0 read path is renting an extra transfer buffer per chunk.");
    }

    private sealed class ArrayPoolRentListener : EventListener
    {
        private const int BufferRentedEventId = 1;

        private readonly int _watchedSize;
        private long _count;

        public ArrayPoolRentListener(int watchedSize) => _watchedSize = watchedSize;

        public long Count => Interlocked.Read(ref _count);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
            {
                EnableEvents(eventSource, EventLevel.Verbose);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // BufferRented payload: bufferId, bufferSize, poolId, bucketId
            if (eventData.EventId == BufferRentedEventId
                && eventData.Payload is { Count: >= 2 }
                && eventData.Payload[1] is int size
                && size == _watchedSize)
            {
                Interlocked.Increment(ref _count);
            }
        }
    }
#endif

    // Returns at most a handful of bytes per read so a chunk takes many reads to fill.
    private sealed class TrickleStream : Stream
    {
        private readonly byte[] _data;
        private readonly Random _random = new(1234);
        private int _position;

        public TrickleStream(byte[] data) => _data = data;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _data.Length - _position;
            if (remaining == 0 || count == 0)
            {
                return 0;
            }

            var n = Math.Min(Math.Min(count, remaining), _random.Next(1, 17));
            Buffer.BlockCopy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
