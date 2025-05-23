using System.Diagnostics;
using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class PublishRetryTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public PublishRetryTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Theory]
    [InlineData(NatsRequestReplyMode.Direct)]
    [InlineData(NatsRequestReplyMode.SharedInbox)]
    public async Task Publish_without_telemetry(NatsRequestReplyMode mode)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url, RequestReplyMode = mode });
        var prefix = _server.GetNextId();

        // Without telemetry
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, headers: headers, cancellationToken: cts.Token);
            Assert.DoesNotContain("Request-Id", headers);
            Assert.DoesNotContain("traceparent", headers);
            Assert.Empty(headers);
        }

        // Without telemetry and data
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, data: 1, headers: headers, cancellationToken: cts.Token);
            Assert.DoesNotContain("Request-Id", headers);
            Assert.DoesNotContain("traceparent", headers);
            Assert.Empty(headers);
        }
    }
}
