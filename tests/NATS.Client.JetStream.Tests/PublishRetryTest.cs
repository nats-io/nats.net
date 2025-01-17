using System.Diagnostics;
using NATS.Client.Core2.Tests;
using NATS.Net;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server-restricted-user")]
public class PublishRetryTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerRestrictedUserFixture _server;

    public PublishRetryTest(ITestOutputHelper output, NatsServerRestrictedUserFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public async Task Publish_with_or_without_telemetry()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            AuthOpts = new NatsAuthOpts { Username = "u" },
        });
        var prefix = _server.GetNextId();

        // With telemetry
        {
            using var activityListener = new ActivityListener { ShouldListenTo = _ => true, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData, };
            ActivitySource.AddActivityListener(activityListener);
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, headers: headers, cancellationToken: cts.Token);
            Assert.Contains("traceparent", headers);
        }

        // Without telemetry
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, headers: headers, cancellationToken: cts.Token);
            Assert.DoesNotContain("traceparent", headers);
            Assert.Empty(headers);
        }

        // With telemetry and data
        {
            using var activityListener = new ActivityListener { ShouldListenTo = _ => true, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData, };
            ActivitySource.AddActivityListener(activityListener);
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, data: 1, headers: headers, cancellationToken: cts.Token);
            Assert.Contains("traceparent", headers);
        }

        // Without telemetry and data
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, data: 1, headers: headers, cancellationToken: cts.Token);
            Assert.DoesNotContain("traceparent", headers);
            Assert.Empty(headers);
        }
    }

    [Fact]
    public async Task Multiple_publish_with_same_headers_when_telemetry_on_should_not_throw_header_readonly_exception()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(activityListener);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            AuthOpts = new NatsAuthOpts { Username = "u" },
        });
        var prefix = _server.GetNextId();

        // Publish with no data implemented in a different method
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, headers: headers, cancellationToken: cts.Token);
            await nats.PublishAsync(prefix, headers: headers, cancellationToken: cts.Token);
            Assert.Contains("traceparent", headers);
        }

        // Also try publish-with-data method
        {
            var headers = new NatsHeaders();
            await nats.PublishAsync(prefix, data: 1, headers: headers, cancellationToken: cts.Token);
            await nats.PublishAsync(prefix, data: 1, headers: headers, cancellationToken: cts.Token);
            Assert.Contains("traceparent", headers);
        }
    }

    [Fact]
    public async Task Retry_with_telemetry_on_should_not_throw_header_readonly_exception()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(activityListener);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var nats = new NatsConnection(new NatsOpts
        {
            Url = _server.Url,
            AuthOpts = new NatsAuthOpts { Username = "u" },
            RequestTimeout = TimeSpan.FromSeconds(.5),
        });
        var prefix = _server.GetNextId();
        var js = nats.CreateJetStreamContext();

        // Because of the permission error (associated user should only have permission to subject 'x' to publish)
        // JetStream publish internally retry publishing again which in turn would use the same header.
        // Telemetry altering headers should not cause 'readonly' exceptions.
        {
            var headers = new NatsHeaders();
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await js.PublishAsync(
                    subject: $"{prefix}.foo",
                    data: "order 1",
                    headers: headers,
                    cancellationToken: cts.Token);
            });
            Assert.IsNotType<InvalidOperationException>(exception);
            Assert.DoesNotMatch("response headers cannot be modified", exception.Message);
            Assert.Contains("traceparent", headers);
        }

        // Also check for specific exception
        {
            var headers = new NatsHeaders();
            await Assert.ThrowsAsync<NatsJSPublishNoResponseException>(async () =>
            {
                await js.PublishAsync(
                    subject: $"{prefix}.foo",
                    data: "order 1",
                    headers: headers,
                    cancellationToken: cts.Token);
            });
            Assert.Contains("traceparent", headers);
        }
    }
}
