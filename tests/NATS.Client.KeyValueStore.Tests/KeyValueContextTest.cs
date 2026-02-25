using NATS.Client.Core.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.KeyValueStore.Tests;

public class KeyValueContextTest
{
    [Fact]
    public async Task Create_store_test()
    {
        var buketName = "kv1";
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var kvStore = await kv.CreateStoreAsync(buketName, cancellationToken);
        kvStore.Bucket.Should().Be(buketName);
    }

    [Fact]
    public async Task Update_store_test()
    {
        var buketName = "kv1";
        var expectedDescription = "Updated description";

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(buketName, cancellationToken);
        var status = await store.GetStatusAsync(cancellationToken);
        status.Info.Config.Description.Should().BeNull();

        var natsKVConfig = new NatsKVConfig(buketName) { Description = expectedDescription };
        await kv.UpdateStoreAsync(natsKVConfig, cancellationToken);
        var updatedStatus = await store.GetStatusAsync(cancellationToken);
        updatedStatus.Info.Config.Description.Should().Be(natsKVConfig.Description);
    }

    [Fact]
    public async Task Create_store_via_create_or_update_store_test()
    {
        const string expectedBuketName = "kv1";
        const string expectedDescription = "description";

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var natsKVConfig = new NatsKVConfig(expectedBuketName) { Description = expectedDescription };
        var store = await kv.CreateOrUpdateStoreAsync(natsKVConfig, cancellationToken);
        var status = await store.GetStatusAsync(cancellationToken);

        status.Bucket.Should().Be(expectedBuketName);
        status.Info.Config.Description.Should().Be(expectedDescription);
    }

    [Fact]
    public async Task Update_store_via_create_or_update_store_test()
    {
        var buketName = "kv1";
        var expectedDescription = "Updated description";

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var store = await kv.CreateStoreAsync(buketName, cancellationToken);
        var status = await store.GetStatusAsync(cancellationToken);
        status.Info.Config.Description.Should().BeNull();

        var natsKVConfig = new NatsKVConfig(buketName) { Description = expectedDescription };
        await kv.CreateOrUpdateStoreAsync(natsKVConfig, cancellationToken);
        var updatedStatus = await store.GetStatusAsync(cancellationToken);
        updatedStatus.Info.Config.Description.Should().Be(natsKVConfig.Description);
    }

    [Fact]
    public async Task Delete_store_test()
    {
        var bucketName = "kv1";

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        await kv.CreateStoreAsync(bucketName, cancellationToken);

        var result = await kv.DeleteStoreAsync(bucketName, cancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Get_bucket_names_test()
    {
        var actualBucketNames = new List<string>();
        var expectedBucketNames = new List<string> { "kv1", "kv2", "kv3" };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        foreach (var bucketName in expectedBucketNames)
        {
            await kv.CreateStoreAsync(new NatsKVConfig(bucketName), cancellationToken);
        }

        await foreach (var bucketName in kv.GetBucketNamesAsync(cancellationToken))
        {
            actualBucketNames.Add(bucketName);
        }

        actualBucketNames.Should().BeEquivalentTo(expectedBucketNames);
    }

    [Fact]
    public async Task Get_statuses_test()
    {
        var bucketName = "kv1";
        var expectedBucketName = $"KV_{bucketName}";
        var expectedMaxBytes = 10_000;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var js = new NatsJSContext(nats);
        var kv = new NatsKVContext(js);

        var natsKVConfig = new NatsKVConfig(bucketName) { MaxBytes = expectedMaxBytes };
        await kv.CreateStoreAsync(natsKVConfig, cancellationToken);

        await foreach (var status in kv.GetStatusesAsync(cancellationToken))
        {
            status.Bucket.Should().Be(expectedBucketName);
            status.Info.Config.MaxBytes.Should().Be(expectedMaxBytes);
        }
    }
}
