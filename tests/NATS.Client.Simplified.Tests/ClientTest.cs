using System.Text;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.Serializers.Json;
using NATS.Net;
using Synadia.Orbit.Testing.NatsServerProcessManager;

// ReSharper disable AccessToDisposedClosure
namespace NATS.Client.Simplified.Tests;

public class ClientTest
{
    [Fact]
    public async Task Client_works_with_all_expected_types_and_falls_back_to_JSON()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var client = new NatsClient(server.Url);

        CancellationTokenSource ctsTestTimeout = new(TimeSpan.FromSeconds(10));
        var ctsStop = CancellationTokenSource.CreateLinkedTokenSource(ctsTestTimeout.Token);

        // Subscribe for int, string, bytes, JSON
        TaskCompletionSource tcs1 = new();
        TaskCompletionSource<int> tcs1data = new();
        var task1 = Task.Run(
            async () =>
            {
                await foreach (var msg in client.SubscribeAsync<int>("x.int.>", cancellationToken: ctsStop.Token))
                {
                    if (msg.Subject.EndsWith("sync"))
                    {
                        tcs1.TrySetResult();
                        continue;
                    }

                    tcs1data.SetResult(msg.Data);
                }
            },
            ctsTestTimeout.Token);

        TaskCompletionSource tcs2 = new();
        TaskCompletionSource<string?> tcs2data = new();
        var task2 = Task.Run(
            async () =>
            {
                await foreach (var msg in client.SubscribeAsync<string>("x.string.>", cancellationToken: ctsStop.Token))
                {
                    if (msg.Subject.EndsWith("sync"))
                    {
                        tcs2.TrySetResult();
                        continue;
                    }

                    tcs2data.SetResult(msg.Data);
                }
            },
            ctsTestTimeout.Token);

        TaskCompletionSource tcs3 = new();
        TaskCompletionSource<byte[]?> tcs3data = new();
        var task3 = Task.Run(
            async () =>
            {
                await foreach (var msg in client.SubscribeAsync<byte[]>("x.bytes.>", cancellationToken: ctsStop.Token))
                {
                    if (msg.Subject.EndsWith("sync"))
                    {
                        tcs3.TrySetResult();
                        continue;
                    }

                    tcs3data.SetResult(msg.Data);
                }
            },
            ctsTestTimeout.Token);

        TaskCompletionSource tcs4 = new();
        TaskCompletionSource<MyData?> tcs4data = new();
        var task4 = Task.Run(
            async () =>
            {
                await foreach (var msg in client.SubscribeAsync<MyData>("x.json.>", cancellationToken: ctsStop.Token))
                {
                    if (msg.Subject.EndsWith("sync"))
                    {
                        tcs4.TrySetResult();
                        continue;
                    }

                    tcs4data.SetResult(msg.Data);
                }
            },
            ctsTestTimeout.Token);

        TaskCompletionSource tcs5 = new();
        var task5 = Task.Run(
            async () =>
            {
                await foreach (var msg in client.SubscribeAsync<MyData>("x.service.>", cancellationToken: ctsStop.Token))
                {
                    if (msg.Subject.EndsWith("sync"))
                    {
                        tcs5.TrySetResult();
                        continue;
                    }

                    if (msg.Data != null)
                    {
                        await msg.ReplyAsync($"Thank you {msg.Data.Name} your Id is {msg.Data.Id}!", cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
                    }
                }
            },
            ctsTestTimeout.Token);

        TaskCompletionSource tcs6 = new();
        var task6 = Task.Run(
            async () =>
            {
                var id = 0;
                await foreach (var msg in client.SubscribeAsync<object>("x.service2.>", cancellationToken: ctsStop.Token))
                {
                    if (msg.Subject.EndsWith("sync"))
                    {
                        tcs6.TrySetResult();
                        continue;
                    }

                    await msg.ReplyAsync(new MyData(id, $"foo{id}"), cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
                    id++;
                }
            },
            ctsTestTimeout.Token);

        await Retry.Until(
            reason: "int synced",
            condition: () =>
            {
                var taskIsCompleted = tcs1.Task.IsCompleted;
                return taskIsCompleted;
            },
            action: () => client.PublishAsync("x.int.sync", cancellationToken: ctsTestTimeout.Token).AsTask());

        await Retry.Until(
            reason: "string synced",
            condition: () => tcs2.Task.IsCompleted,
            action: () => client.PublishAsync("x.string.sync", cancellationToken: ctsTestTimeout.Token).AsTask());

        await Retry.Until(
            reason: "bytes synced",
            condition: () => tcs3.Task.IsCompleted,
            action: () => client.PublishAsync("x.bytes.sync", cancellationToken: ctsTestTimeout.Token).AsTask());

        await Retry.Until(
            reason: "json synced",
            condition: () => tcs4.Task.IsCompleted,
            action: () => client.PublishAsync("x.json.sync", cancellationToken: ctsTestTimeout.Token).AsTask());

        await Retry.Until(
            reason: "service synced",
            condition: () => tcs5.Task.IsCompleted,
            action: () => client.PublishAsync("x.service.sync", cancellationToken: ctsTestTimeout.Token).AsTask());

        await Retry.Until(
            reason: "service2 synced",
            condition: () => tcs6.Task.IsCompleted,
            action: () => client.PublishAsync("x.service2.sync", cancellationToken: ctsTestTimeout.Token).AsTask());

        await client.PublishAsync("x.int.data", 100, cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
        var r1 = await tcs1data.Task.ConfigureAwait(true);
        Assert.Equal(100, r1);

        await client.PublishAsync("x.string.data", "Hello, World!", cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
        var r2 = await tcs2data.Task.ConfigureAwait(true);
        Assert.Equal("Hello, World!", r2);

        await client.PublishAsync("x.bytes.data", "ABC"u8.ToArray(), cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
        var r3 = await tcs3data.Task.ConfigureAwait(true);
        Assert.Equal(Encoding.UTF8.GetBytes("ABC"), r3);

        await client.PublishAsync("x.json.data", new MyData(30, "bar"), cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
        var r4 = await tcs4data.Task.ConfigureAwait(true);
        Assert.Equal(new MyData(30, "bar"), r4);

        // Request/Reply
        {
            var response = await client.RequestAsync<MyData, string>("x.service.call", new MyData(100, "foo"), cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
            Assert.Equal("Thank you foo your Id is 100!", response.Data);
        }

        // Request/Reply without request data
        for (var i = 0; i < 3; i++)
        {
            var response = await client.RequestAsync<MyData>("x.service2.call", cancellationToken: ctsTestTimeout.Token).ConfigureAwait(true);
            Assert.NotNull(response.Data);
            Assert.Equal(i, response.Data.Id);
            Assert.Equal($"foo{i}", response.Data.Name);
        }

        // Use JetStream by referencing NATS.Client.JetStream package
        var js = client.CreateJetStreamContext();
        await js.CreateStreamAsync(new StreamConfig("test", ["test.>"]), ctsTestTimeout.Token).ConfigureAwait(true);
        await foreach (var stream in js.ListStreamsAsync(cancellationToken: ctsTestTimeout.Token))
        {
            Assert.Equal("test", stream.Info.Config.Name);
        }

        ctsStop.Cancel();

        await Task.WhenAll(task1, task2, task3, task4, task5, task6).ConfigureAwait(true);
    }

    [Fact]
    public void Client_opts_default_regitry()
    {
        var client = new NatsClient(new NatsOpts());
        Assert.Equal(NatsClientDefaultSerializerRegistry.Default, client.Connection.Opts.SerializerRegistry);
    }

    [Fact]
    public void Client_opts_custom_registry()
    {
        var registry = new NatsJsonSerializerRegistry();
        var client = new NatsClient(new NatsOpts { SerializerRegistry = registry });
        Assert.Equal(registry, client.Connection.Opts.SerializerRegistry);
    }

    [Fact]
    public void Client_opts_default_pending()
    {
        var client = new NatsClient(new NatsOpts());
        Assert.Equal(BoundedChannelFullMode.Wait, client.Connection.Opts.SubPendingChannelFullMode);
    }

    [Fact]
    public void Client_opts_set_pending()
    {
        var client = new NatsClient(new NatsOpts(), pending: BoundedChannelFullMode.DropNewest);
        Assert.Equal(BoundedChannelFullMode.DropNewest, client.Connection.Opts.SubPendingChannelFullMode);
    }

    private record MyData(int Id, string Name);
}
