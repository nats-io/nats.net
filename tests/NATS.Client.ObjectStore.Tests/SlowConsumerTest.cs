using NATS.Client.Core.Tests;
using NATS.Client.ObjectStore.Models;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.ObjectStore.Tests;

public class SlowConsumerTest
{
    private readonly ITestOutputHelper _output;

    public SlowConsumerTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ObjectStore_get_slow_consumer_should_not_block_connection()
    {
        // This test verifies that when an Object Store consumer is slow (not reading chunks),
        // the connection should NOT be blocked. Pings and pub/sub should continue to work.
        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = testCts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = server.Url,
            SubPendingChannelCapacity = 10,
        });
        await nats.ConnectAsync();

        var js = new NatsJSContext(nats);
        var ob = new NatsObjContext(js);

        var consumerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var droppedCount = 0;
        nats.MessageDropped += (_, _) =>
        {
            Interlocked.Increment(ref droppedCount);
            return default;
        };

        var store = await ob.CreateObjectStoreAsync("test-bucket", cancellationToken);

        // Create a large object with many small chunks to flood the channel
        // Use small chunk size (1KB) to create many messages
        const int chunkSize = 1024;
        const int objectSize = 128 * 1024; // 128KB = 128 chunks
        var largeData = new byte[objectSize];
        Random.Shared.NextBytes(largeData);

        var meta = new ObjectMetadata
        {
            Name = "large-object",
            Options = new MetaDataOptions { MaxChunkSize = chunkSize },
        };

        await store.PutAsync(meta, new MemoryStream(largeData), cancellationToken: cancellationToken);
        _output.WriteLine($"Created object with {objectSize} bytes in {objectSize / chunkSize} chunks");

        // Use a slow stream that blocks after reading first chunk
        var slowStream = new SlowStream(_output, consumerCts.Token);
        var readStarted = slowStream.FirstWriteReceived;

        // Start reading into the slow stream - this will block after first chunk
        var readTask = Task.Run(
            async () =>
            {
                try
                {
                    // GetAsync writes to the stream as chunks arrive
                    // Our slow stream will block after first write
                    await store.GetAsync("large-object", slowStream, cancellationToken: consumerCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected when we cancel
                }
            },
            consumerCts.Token);

        // Wait for first chunk to arrive
        var startTimeout = Task.Delay(TimeSpan.FromSeconds(10));
        var startResult = await Task.WhenAny(readStarted.Task, startTimeout);

        if (startResult == startTimeout)
        {
            _output.WriteLine("First chunk did not arrive in time");
            Assert.Fail("First chunk did not arrive in time");
        }

        _output.WriteLine("First chunk received, stream is now blocking");
        await Task.Delay(500); // Give time for channel to fill up

        // Run sequential pings - these should NOT be blocked
        var pingCount = 0;
        var pingErrors = 0;
        var maxPingRttMs = 0.0;

        for (var i = 0; i < 20; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var pingTask = nats.PingAsync().AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(pingTask, timeoutTask);

            sw.Stop();

            if (completed == timeoutTask)
            {
                _output.WriteLine($"Ping {i + 1}: TIMEOUT after {sw.ElapsedMilliseconds}ms - socket reader blocked!");
                pingErrors++;
                break;
            }

            try
            {
                var rtt = await pingTask;
                pingCount++;
                if (rtt.TotalMilliseconds > maxPingRttMs)
                    maxPingRttMs = rtt.TotalMilliseconds;
                _output.WriteLine($"Ping {i + 1}: RTT {rtt.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                pingErrors++;
                _output.WriteLine($"Ping {i + 1} error: {ex.Message}");
            }

            await Task.Delay(100);
        }

        _output.WriteLine($"Pings succeeded: {pingCount}, failed: {pingErrors}, max RTT: {maxPingRttMs}ms");
        _output.WriteLine($"Messages dropped: {droppedCount}");

        // Test pub/sub to verify regular messages flow
        var pubSubReceived = 0;
        var pubSubSubject = "pubsub.test";

        var pubSubTimeout = Task.Delay(TimeSpan.FromSeconds(5));
        var pubSubTask = Task.Run(
            async () =>
            {
                await using var sub = await nats.SubscribeCoreAsync<string>(pubSubSubject);

                for (var i = 0; i < 10; i++)
                {
                    await nats.PublishAsync(pubSubSubject, $"msg-{i}");
                }

                await foreach (var msg in sub.Msgs.ReadAllAsync())
                {
                    Interlocked.Increment(ref pubSubReceived);
                    _output.WriteLine($"Pub/Sub received: {msg.Data}");
                    if (Volatile.Read(ref pubSubReceived) >= 10)
                        break;
                }
            });

        var pubSubCompleted = await Task.WhenAny(pubSubTask, pubSubTimeout);
        if (pubSubCompleted == pubSubTimeout)
        {
            _output.WriteLine($"Pub/Sub timed out after receiving {pubSubReceived} messages - socket reader blocked!");
        }

        _output.WriteLine($"Pub/Sub messages received: {pubSubReceived}/10");

        // Assertions - verify BEFORE cleanup since cleanup may hang on slow stream:
        // 1. All pings should succeed (connection not blocked)
        Assert.Equal(20, pingCount);
        Assert.Equal(0, pingErrors);

        // 2. Ping RTT should be reasonable (not blocked for seconds)
        Assert.True(maxPingRttMs < 1000, $"Ping RTT too high ({maxPingRttMs}ms), socket reader may be blocked");

        // 3. Pub/sub messages should flow (socket reader not blocked)
        Assert.Equal(10, pubSubReceived);

        // Cleanup - cancel the slow consumer, don't wait for it
        consumerCts.Cancel();
    }

    /// <summary>
    /// A stream that blocks after receiving the first write, simulating a slow consumer.
    /// </summary>
    private class SlowStream : Stream
    {
        private readonly ITestOutputHelper _output;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource _firstWriteReceived = new();
        private int _writeCount;

        public SlowStream(ITestOutputHelper output, CancellationToken cancellationToken)
        {
            _output = output;
            _cancellationToken = cancellationToken;
        }

        public TaskCompletionSource FirstWriteReceived => _firstWriteReceived;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position
        {
            get => 0; set { }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, _cancellationToken).GetAwaiter().GetResult();
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var writeNum = Interlocked.Increment(ref _writeCount);
            _output.WriteLine($"SlowStream.WriteAsync: chunk {writeNum}, {count} bytes");

            if (writeNum == 1)
            {
                // Signal that we received the first chunk
                _firstWriteReceived.TrySetResult();
            }

            // Block on all subsequent writes - simulating slow consumer
            if (writeNum > 1)
            {
                _output.WriteLine($"SlowStream: blocking on chunk {writeNum}");
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask(WriteAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken));
        }
    }
}
