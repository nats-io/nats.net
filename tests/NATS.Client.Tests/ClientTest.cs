using System.Text;
using NATS.Client.Core.Tests;

// ReSharper disable AccessToDisposedClosure
namespace NATS.Client.Tests;

public class ClientTest
{
    [Fact]
    public async Task Client_works_with_all_expected_types_and_falls_back_to_JSON()
    {
        await using var server = NatsServer.Start();
        await using var client = new NatsClient(server.ClientUrl);

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
                        await msg.ReplyAsync($"Thank you {msg.Data.Name} your Id is {msg.Data.Id}!", cancellationToken: ctsTestTimeout.Token);
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

                    await msg.ReplyAsync(new MyData(id, $"foo{id}"), cancellationToken: ctsTestTimeout.Token).ConfigureAwait(false);
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

        await client.PublishAsync("x.int.data", 100, cancellationToken: ctsTestTimeout.Token);
        var r1 = await tcs1data.Task;
        Assert.Equal(100, r1);

        await client.PublishAsync("x.string.data", "Hello, World!", cancellationToken: ctsTestTimeout.Token);
        var r2 = await tcs2data.Task;
        Assert.Equal("Hello, World!", r2);

        await client.PublishAsync("x.bytes.data", "ABC"u8.ToArray(), cancellationToken: ctsTestTimeout.Token);
        var r3 = await tcs3data.Task;
        Assert.Equal(Encoding.UTF8.GetBytes("ABC"), r3);

        await client.PublishAsync("x.json.data", new MyData(30, "bar"), cancellationToken: ctsTestTimeout.Token);
        var r4 = await tcs4data.Task;
        Assert.Equal(new MyData(30, "bar"), r4);

        // Request/Reply
        {
            var response = await client.RequestAsync<MyData, string>("x.service.call", new MyData(100, "foo"), cancellationToken: ctsTestTimeout.Token);
            Assert.Equal("Thank you foo your Id is 100!", response.Data);
        }

        // Request/Reply without request data
        for (var i = 0; i < 3; i++)
        {
            var response = await client.RequestAsync<MyData>("x.service2.call", cancellationToken: ctsTestTimeout.Token);
            Assert.NotNull(response.Data);
            Assert.Equal(i, response.Data.Id);
            Assert.Equal($"foo{i}", response.Data.Name);
        }

        // Use JetStream by referencing NATS.Client.JetStream package
        // var js = client.GetJetStream();
        ctsStop.Cancel();

        await Task.WhenAll(task1, task2, task3, task4, task5, task6);
    }

    private record MyData(int Id, string Name);
}
