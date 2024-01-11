namespace NATS.Client.Core.Tests;

public class MessageInterfaceTest
{
    [Fact]
    public async Task Sub_custom_builder_test()
    {
        await using var server = NatsServer.Start();
        await using var nats = server.CreateClientConnection();

        var sync = 0;
        var sub = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var natsMsg in nats.SubscribeAsync<string>("foo.*"))
            {
                if (natsMsg.Subject == "foo.sync")
                {
                    Interlocked.Increment(ref sync);
                    continue;
                }

                if (natsMsg.Subject == "foo.end")
                {
                    break;
                }

                // Boxing allocation: conversion from 'NatsMsg<string>' to 'INatsMsg<string>' requires boxing of the value type
                //                      vvvvvvv
                ProcessMessage(count++, natsMsg);
            }
        });

        await Retry.Until(
            reason: "subscription is ready",
            condition: () => Volatile.Read(ref sync) > 0,
            action: async () => await nats.PublishAsync("foo.sync"),
            retryDelay: TimeSpan.FromSeconds(1));

        for (var i = 0; i < 10; i++)
            await nats.PublishAsync(subject: $"foo.{i}", data: $"test_msg_{i}");

        await nats.PublishAsync("foo.end");

        await sub;
    }

    private void ProcessMessage(int count, INatsMsg<string> natsMsg) => natsMsg.Data.Should().Be($"test_msg_{count}");
}
