using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ListTests
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public ListTests(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task List_streams()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestTimeout = TimeSpan.FromSeconds(5) });
        var prefix = _server.GetNextId() + "-";
        _output.WriteLine($"prefix: {prefix}");

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        const int total = 120;

        for (var i = 0; i < total; i++)
        {
            await js.CreateStreamAsync(new StreamConfig($"{prefix}s{i:D5}", [$"{prefix}s{i:D5}.*"]), cts.Token);
        }

        // Stream names
        {
            var names = new List<string>();

            await foreach (var stream in js.ListStreamNamesAsync(cancellationToken: cts.Token))
            {
                if (stream.StartsWith(prefix))
                    names.Add(stream);
            }

            Assert.Equal(total, names.Count);

            names.Sort();

            for (var i = 0; i < total; i++)
            {
                Assert.Equal($"{prefix}s{i:D5}", names[i]);
            }

            var noNames = 0;
            await foreach (var stream in js.ListStreamNamesAsync(subject: "none-existent", cancellationToken: cts.Token))
            {
                noNames++;
            }

            Assert.Equal(0, noNames);
        }

        // Streams
        {
            var streams = new List<INatsJSStream>();
            await foreach (var stream in js.ListStreamsAsync(cancellationToken: cts.Token))
            {
                if (stream.Info.Config.Name!.StartsWith(prefix))
                    streams.Add(stream);
            }

            Assert.Equal(total, streams.Count);

            streams.Sort((a, b) => string.CompareOrdinal(a.Info.Config.Name, b.Info.Config.Name));

            for (var i = 0; i < total; i++)
            {
                Assert.Equal($"{prefix}s{i:D5}", streams[i].Info.Config.Name);
            }

            var noNames = 0;
            await foreach (var stream in js.ListStreamNamesAsync(subject: "none-existent", cancellationToken: cts.Token))
            {
                noNames++;
            }

            Assert.Equal(0, noNames);
        }
    }

    [Fact]
    public async Task List_consumers()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestTimeout = TimeSpan.FromSeconds(5) });
        var prefix = _server.GetNextId() + "-";
        _output.WriteLine($"prefix: {prefix}");
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var stream = await js.CreateStreamAsync(new StreamConfig($"{prefix}s1", [$"{prefix}s1.*"]), cts.Token);

        const int total = 1200;

        for (var i = 0; i < total; i++)
        {
            await js.CreateOrUpdateConsumerAsync($"{prefix}s1", new ConsumerConfig($"{prefix}c{i:D5}"), cts.Token);
        }

        // List names
        {
            var names = new List<string>();
            await foreach (var name in stream.ListConsumerNamesAsync(cts.Token))
            {
                names.Add(name);
            }

            names.Sort();

            Assert.Equal(total, names.Count);

            for (var i = 0; i < total; i++)
            {
                Assert.Equal($"{prefix}c{i:D5}", names[i]);
            }
        }

        // List consumers
        {
            var consumers = new List<INatsJSConsumer>();
            await foreach (var name in stream.ListConsumersAsync(cts.Token))
            {
                consumers.Add(name);
            }

            consumers.Sort((a, b) => string.CompareOrdinal(a.Info.Name, b.Info.Name));

            Assert.Equal(total, consumers.Count);

            for (var i = 0; i < total; i++)
            {
                Assert.Equal($"{prefix}c{i:D5}", consumers[i].Info.Name);
            }
        }

        // Empty list
        {
            var stream2 = await js.CreateStreamAsync(new StreamConfig($"{prefix}s2", [$"{prefix}s2.*"]), cts.Token);

            var count = 0;
            await foreach (var unused in stream2.ListConsumersAsync(cts.Token))
            {
                count++;
            }

            await foreach (var unused in stream2.ListConsumerNamesAsync(cts.Token))
            {
                count++;
            }

            Assert.Equal(0, count);
        }
    }
}
