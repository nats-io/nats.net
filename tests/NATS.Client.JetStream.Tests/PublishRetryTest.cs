using System.Diagnostics;
using NATS.Client.Core2.Tests;
using NATS.Client.TestUtilities2;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server-restricted-user")]
public class PublishRetryTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerRestrictedUserFixture _server;
    private readonly bool _isFramework;

    public PublishRetryTest(ITestOutputHelper output, NatsServerRestrictedUserFixture server)
    {
        _output = output;
        _server = server;
        _isFramework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Contains("Framework");
    }

    [Fact]
    public async Task Publish_without_telemetry()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            AuthOpts = new NatsAuthOpts { Username = "u" },
        });
        var prefix = _server.GetNextId();

        // Without telemetry
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, headers: headers, cancellationToken: cts.Token);
            Assert.DoesNotContain(_isFramework ? "Request-Id" : "traceparent", headers);
            Assert.Empty(headers);
        }

        // Without telemetry and data
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, data: 1, headers: headers, cancellationToken: cts.Token);
            Assert.DoesNotContain(_isFramework ? "Request-Id" : "traceparent", headers);
            Assert.Empty(headers);
        }
    }
}
