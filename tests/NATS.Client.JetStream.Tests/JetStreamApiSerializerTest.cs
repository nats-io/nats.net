using System.Buffers;
using System.Text;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class JetStreamApiSerializerTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public JetStreamApiSerializerTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Should_respect_buffers_lifecycle()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);
        var apiSubject = $"{prefix}.js.fake.api";
        var dataSubject = $"{prefix}.data";

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ctsDone = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);

        List<Task> tasks = new();

        // Keep reader buffers busy with lots of data which should not be
        // kept around and used by the JsonDocument deserializer.
        // Data reader
        tasks.Add(Task.Run(
            async () =>
            {
                await foreach (var unused in nats.SubscribeAsync<string>(dataSubject, cancellationToken: ctsDone.Token))
                {
                }
            },
            cts.Token));

        // Data writer
        tasks.Add(Task.Run(
            async () =>
            {
                var data = new string('x', 1024);
                while (ctsDone.IsCancellationRequested == false)
                {
                    await nats.PublishAsync(dataSubject, data, cancellationToken: ctsDone.Token);
                }
            },
            cts.Token));

        // Fake JS API responder
        tasks.Add(Task.Run(
            async () =>
            {
                var json = JsonSerializer.Serialize(new AccountInfoResponse { Consumers = 1234 });
                await foreach (var msg in nats.SubscribeAsync<object>(apiSubject, cancellationToken: ctsDone.Token))
                {
                    await msg.ReplyAsync(json, cancellationToken: cts.Token);
                }
            },
            cts.Token));

        // Fake JS API requester
        tasks.Add(Task.Run(
            async () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    if (ctsDone.IsCancellationRequested)
                        return;

                    try
                    {
                        var result = await js.TryJSRequestAsync<object, AccountInfoResponse>(apiSubject, null, ctsDone.Token);
                    }
                    catch
                    {
                        ctsDone.Cancel();
                        throw;
                    }
                }

                ctsDone.Cancel();
            },
            cts.Token));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (TaskCanceledException)
        {
        }
    }

    [Fact]
    public void Deserialize_value()
    {
        var serializer = NatsJSJsonDocumentSerializer<AccountInfoResponse>.Default;
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("""{"memory":1}""")));
        result.Value.Memory.Should().Be(1);
    }

    [Fact]
    public void Deserialize_empty_buffer()
    {
        var serializer = NatsJSJsonDocumentSerializer<AccountInfoResponse>.Default;
        var result = serializer.Deserialize(ReadOnlySequence<byte>.Empty);
        result.Exception.Message.Should().Be("Buffer is empty");
    }

    [Fact]
    public void Deserialize_error()
    {
        var serializer = NatsJSJsonDocumentSerializer<AccountInfoResponse>.Default;
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("""{"error":{"code":2}}""")));
        result.Error.Code.Should().Be(2);
    }
}
