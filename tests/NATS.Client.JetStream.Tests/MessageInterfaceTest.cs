using NATS.Client.Core.Tests;
using NATS.Client.TestUtilities2;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

public class MessageInterfaceTest
{
    [Fact]
    public async Task Using_message_interface()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await js.CreateStreamAsync("s1", ["s1.*"], cts.Token);

        var ack = await js.PublishAsync("s1.foo", "test_msg", cancellationToken: cts.Token);
        ack.EnsureSuccess();

        var consumer = await js.CreateOrUpdateConsumerAsync("s1", "c1", cancellationToken: cts.Token);

        await foreach (var natsJSMsg in consumer.ConsumeAsync<string>(cancellationToken: cts.Token))
        {
            // Boxing allocation: conversion from 'NatsJSMsg<string>' to 'INatsJSMsg<string>' requires boxing of the value type
            //                        vvvvvvvvv
            await ProcessMessageAsync(natsJSMsg, cts.Token);
            break;
        }

        await Retry.Until(
            "ack pending 0",
            async () =>
            {
                var c = await js.GetConsumerAsync("s1", "c1", cts.Token);
                return c.Info.NumAckPending == 0;
            },
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(20));
        await consumer.RefreshAsync(cts.Token);
        Assert.Equal(0, consumer.Info.NumAckPending);
    }

    private async Task ProcessMessageAsync(INatsJSMsg<string> natsJSMsg, CancellationToken cancellationToken = default)
    {
        natsJSMsg.Data.Should().Be("test_msg");
        await natsJSMsg.AckAsync(cancellationToken: cancellationToken);
    }
}
