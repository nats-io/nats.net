using System.Diagnostics;
using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ConsumerNotificationTest
{
    [SkipOnPlatform("WINDOWS", "doesn't support signals")]
    public async Task Non_terminal_errors_sent_as_notifications()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);
        (await js.PublishAsync("s1.1", 1, cancellationToken: cts.Token)).EnsureSuccess();

        var consumer1 = await js.CreateOrUpdateConsumerAsync(stream: "s1", config: new ConsumerConfig("c1"), cancellationToken: cts.Token);
        var consumer2 = await js.CreateOrUpdateConsumerAsync(stream: "s1", config: new ConsumerConfig("c2"), cancellationToken: cts.Token);

        var cts1 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var natsJSConsumeOpts = new NatsJSConsumeOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (notification, _) =>
            {
                if (notification is NatsJSProtocolNotification { HeaderCode: 409, HeaderMessageText: "Server Shutdown" })
                {
                    cts1.Cancel();
                }

                return Task.CompletedTask;
            },
        };

        var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var natsJSFetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 10,
            NotificationHandler = (notification, _) =>
            {
                if (notification is NatsJSProtocolNotification { HeaderCode: 409, HeaderMessageText: "Server Shutdown" })
                {
                    cts2.Cancel();
                }

                return Task.CompletedTask;
            },
        };

        var signal1 = new WaitSignal();

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var unused in consumer1.ConsumeAsync<int>(opts: natsJSConsumeOpts, cancellationToken: cts1.Token))
            {
                signal1.Pulse();
            }
        });

        var signal2 = new WaitSignal();

        var fetchTask = Task.Run(async () =>
        {
            await foreach (var unused in consumer2.FetchAsync<int>(opts: natsJSFetchOpts, cancellationToken: cts2.Token))
            {
                signal2.Pulse();
            }
        });

        await signal1;
        await signal2;

        // SIGTERM: Stops the server gracefully
        Process.Start("kill", $"-TERM {server.ServerProcess!.Id}");

        await Task.WhenAll(consumeTask, fetchTask);
    }

    [Fact]
    public async Task Exceeded_max_errors()
    {
        await using var server = NatsServer.StartJS();
        await using var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", new[] { "s1.*" }, cts.Token);

        // 409 Exceeded MaxRequestBatch
        await ConsumeAndFetchTerminatesAsync(
            js,
            new ConsumerConfig("c1") { MaxBatch = 10, },
            new NatsJSConsumeOpts { MaxMsgs = 20, },
            new NatsJSFetchOpts { MaxMsgs = 20, },
            expectedCode: 409,
            expectedMessage: "Exceeded MaxRequestBatch of 10",
            cts.Token);

        // 409 Exceeded MaxRequestExpires
        await ConsumeAndFetchTerminatesAsync(
            js,
            new ConsumerConfig("c2") { MaxExpires = TimeSpan.FromSeconds(10), },
            new NatsJSConsumeOpts { MaxMsgs = 20, Expires = TimeSpan.FromSeconds(20) },
            new NatsJSFetchOpts { MaxMsgs = 20, Expires = TimeSpan.FromSeconds(20) },
            expectedCode: 409,
            expectedMessage: "Exceeded MaxRequestExpires of 10s",
            cts.Token);

        // 409 Exceeded MaxRequestMaxBytes
        await ConsumeAndFetchTerminatesAsync(
            js,
            new ConsumerConfig("c3") { MaxBytes = 1024, },
            new NatsJSConsumeOpts { MaxBytes = 2048, },
            new NatsJSFetchOpts { MaxBytes = 2048, },
            expectedCode: 409,
            expectedMessage: "Exceeded MaxRequestMaxBytes of 1024",
            cts.Token);
    }

    private async Task ConsumeAndFetchTerminatesAsync(
        NatsJSContext js,
        ConsumerConfig consumerConfig,
        NatsJSConsumeOpts natsJSConsumeOpts,
        NatsJSFetchOpts natsJSFetchOpts,
        int expectedCode,
        string expectedMessage,
        CancellationToken cancellationToken)
    {
        var consumer = await js.CreateOrUpdateConsumerAsync(
            stream: "s1",
            config: consumerConfig,
            cancellationToken: cancellationToken);

        // consume
        {
            var e = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
            {
                await foreach (var unused in consumer.ConsumeAsync<int>(opts: natsJSConsumeOpts, cancellationToken: cancellationToken))
                {
                }
            });

            Assert.Equal(expectedCode, e.HeaderCode);
            Assert.Equal(expectedMessage, e.HeaderMessageText);
        }

        // fetch
        {
            var e = await Assert.ThrowsAsync<NatsJSProtocolException>(async () =>
            {
                await foreach (var unused in consumer.FetchAsync<int>(opts: natsJSFetchOpts, cancellationToken: cancellationToken))
                {
                }
            });

            Assert.Equal(expectedCode, e.HeaderCode);
            Assert.Equal(expectedMessage, e.HeaderMessageText);
        }
    }
}
