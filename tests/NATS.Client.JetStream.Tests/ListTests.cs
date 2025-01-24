using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ListTests
{
    private readonly ITestOutputHelper _output;

    public ListTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task List_streams()
    {
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(5) });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        const int total = 1200;

        for (var i = 0; i < total; i++)
        {
            await js.CreateStreamAsync(new StreamConfig($"s{i:D5}", new[] { $"s{i:D5}.*" }), cts.Token);
        }

        // Stream names
        {
            var names = new List<string>();

            await foreach (var stream in js.ListStreamNamesAsync(cancellationToken: cts.Token))
            {
                names.Add(stream);
            }

            Assert.Equal(total, names.Count);

            names.Sort();

            for (var i = 0; i < total; i++)
            {
                Assert.Equal($"s{i:D5}", names[i]);
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
                streams.Add(stream);
            }

            Assert.Equal(total, streams.Count);

            streams.Sort((a, b) => string.CompareOrdinal(a.Info.Config.Name, b.Info.Config.Name));

            for (var i = 0; i < total; i++)
            {
                Assert.Equal($"s{i:D5}", streams[i].Info.Config.Name);
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
        await using var server = await NatsServer.StartJSAsync();
        await using var nats = await server.CreateClientConnectionAsync(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(5) });
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stream = await js.CreateStreamAsync(new StreamConfig("s1", new[] { "s1.*" }), cts.Token);

        const int total = 1200;

        for (var i = 0; i < total; i++)
        {
            await js.CreateOrUpdateConsumerAsync("s1", new ConsumerConfig($"c{i:D5}"), cts.Token);
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
                Assert.Equal($"c{i:D5}", names[i]);
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
                Assert.Equal($"c{i:D5}", consumers[i].Info.Name);
            }
        }

        // Empty list
        {
            var stream2 = await js.CreateStreamAsync(new StreamConfig("s2", new[] { "s2.*" }), cts.Token);

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
