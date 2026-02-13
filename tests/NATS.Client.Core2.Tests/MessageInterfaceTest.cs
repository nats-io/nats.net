using NATS.Client.Core2.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

[Collection("nats-server")]
public class MessageInterfaceTest
{
    private readonly NatsServerFixture _server;

    public MessageInterfaceTest(NatsServerFixture server)
    {
        _server = server;
    }

    [Fact]
    public async Task Sub_custom_builder_test()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });

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
