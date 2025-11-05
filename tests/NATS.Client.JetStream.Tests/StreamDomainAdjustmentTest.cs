using System.Text.Json;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class StreamDomainAdjustmentTest(NatsServerFixture server)
{
    [Fact]
    public async Task Stream_operations_should_convert_domain_to_external_api()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        // Track captured payloads for each operation
        var capturedPayloads = new List<string>();

        await using var ms = new MockServer((_, cmd) =>
        {
            if (cmd.Name == "PUB")
            {
                // Capture the payload sent by the client
                var payload = new string(cmd.Buffer!);

                if (cmd.Subject.Contains("STREAM."))
                {
                    capturedPayloads.Add(payload);

                    // Return minimal valid response
                    cmd.Reply(payload: """{"config":{"name":"s1"},"state":{"messages":0}}""");
                }
            }

            return Task.CompletedTask;
        });

        await using var nats = new NatsConnection(new NatsOpts { Url = ms.Url });
        var js = new NatsJSContext(nats);

        // Configure a stream with Source that has Domain set
        var streamConfig = new StreamConfig
        {
            Name = "s1",
            Subjects = ["s1"],
            Sources =
            [
                new StreamSource
                {
                    Name = "x1",
                    Domain = "TEST_DOMAIN",
                }
            ],
        };

        // Test all the calls
        await js.CreateStreamAsync(streamConfig, cancellationToken);
        await js.UpdateStreamAsync(streamConfig, cancellationToken);
        await js.CreateOrUpdateStreamAsync(streamConfig, cancellationToken);

        // Verify all payloads were captured
        Assert.Equal(3, capturedPayloads.Count);

        // Verify each payload has the correct domain conversion
        foreach (var payload in capturedPayloads)
        {
            // Parse the JSON payload
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // Verify sources array exists
            Assert.True(root.TryGetProperty("sources", out var sources));
            Assert.True(sources.GetArrayLength() > 0);

            var firstSource = sources[0];

            // Verify domain field does NOT exist (it's client-side only, JsonIgnore)
            Assert.False(firstSource.TryGetProperty("domain", out _));

            // Verify the external.api field exists and has the correct value
            Assert.True(firstSource.TryGetProperty("external", out var external));
            Assert.True(external.TryGetProperty("api", out var api));
            Assert.Equal("$JS.TEST_DOMAIN.API", api.GetString());
        }
    }

    [Fact]
    public async Task Create_stream_with_domain_on_sources_should_populate_external_api()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Create an aggregate stream with Domain on Sources
        var streamConfig = new StreamConfig($"{prefix}aggregate", [$"{prefix}aggregate.*"])
        {
            Sources =
            [
                new StreamSource
                {
                    Name = $"{prefix}source",
                    Domain = "TEST_DOMAIN",
                }
            ],
        };

        var stream = await js.CreateStreamAsync(streamConfig, cts.Token);

        // Verify the returned config has External.Api set, Domain is not preserved
        Assert.NotNull(stream.Info.Config.Sources);
        Assert.Single(stream.Info.Config.Sources);
        var source = stream.Info.Config.Sources.First();
        Assert.NotNull(source.External);
        Assert.Equal("$JS.TEST_DOMAIN.API", source.External.Api);

        // Get stream back and verify again
        var retrievedStream = await js.GetStreamAsync($"{prefix}aggregate", cancellationToken: cts.Token);
        Assert.NotNull(retrievedStream.Info.Config.Sources);
        var retrievedSource = retrievedStream.Info.Config.Sources.First();
        Assert.NotNull(retrievedSource.External);
        Assert.Equal("$JS.TEST_DOMAIN.API", retrievedSource.External.Api);
    }

    [Fact]
    public async Task Update_stream_to_add_domain_on_sources_should_populate_external_api()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Create aggregate stream WITHOUT Domain
        var streamConfig = new StreamConfig($"{prefix}aggregate", [$"{prefix}aggregate.*"])
        {
            Sources =
            [
                new StreamSource
                {
                    Name = $"{prefix}source",
                }
            ],
        };

        var stream = await js.CreateStreamAsync(streamConfig, cts.Token);

        // Verify initially no External
        Assert.NotNull(stream.Info.Config.Sources);
        var source = stream.Info.Config.Sources.First();
        Assert.Null(source.External);

        // Update to add Domain
        var updatedConfig = streamConfig with
        {
            Sources =
            [
                new StreamSource
                {
                    Name = $"{prefix}source",
                    Domain = "TEST_DOMAIN",
                }
            ],
        };

        var updatedStream = await js.UpdateStreamAsync(updatedConfig, cts.Token);

        // Verify External.Api is now set
        Assert.NotNull(updatedStream.Info.Config.Sources);
        var updatedSource = updatedStream.Info.Config.Sources.First();
        Assert.NotNull(updatedSource.External);
        Assert.Equal("$JS.TEST_DOMAIN.API", updatedSource.External.Api);

        // Get stream back and verify
        var retrievedStream = await js.GetStreamAsync($"{prefix}aggregate", cancellationToken: cts.Token);
        Assert.NotNull(retrievedStream.Info.Config.Sources);
        var retrievedSource = retrievedStream.Info.Config.Sources.First();
        Assert.NotNull(retrievedSource.External);
        Assert.Equal("$JS.TEST_DOMAIN.API", retrievedSource.External.Api);
    }

    [Fact]
    public async Task CreateOrUpdate_stream_with_domain_on_sources_should_populate_external_api()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var prefix = server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // CreateOrUpdate aggregate stream with Domain on Sources
        var streamConfig = new StreamConfig($"{prefix}aggregate", [$"{prefix}aggregate.*"])
        {
            Sources =
            [
                new StreamSource
                {
                    Name = $"{prefix}source",
                    Domain = "TEST_DOMAIN",
                }
            ],
        };

        var stream = await js.CreateOrUpdateStreamAsync(streamConfig, cts.Token);

        // Verify the config has External.Api set
        Assert.NotNull(stream.Info.Config.Sources);
        Assert.Single(stream.Info.Config.Sources);
        var source = stream.Info.Config.Sources.First();
        Assert.NotNull(source.External);
        Assert.Equal("$JS.TEST_DOMAIN.API", source.External.Api);

        // Get stream back and verify
        var retrievedStream = await js.GetStreamAsync($"{prefix}aggregate", cancellationToken: cts.Token);
        Assert.NotNull(retrievedStream.Info.Config.Sources);
        var retrievedSource = retrievedStream.Info.Config.Sources.First();
        Assert.NotNull(retrievedSource.External);
        Assert.Equal("$JS.TEST_DOMAIN.API", retrievedSource.External.Api);
    }
}
