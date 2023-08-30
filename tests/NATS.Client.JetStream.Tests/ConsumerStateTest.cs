using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Tests;

public class ConsumerStateTest
{
    [Fact]
    public void Default_options()
    {
        var m = NatsJSOpsDefaults.SetMax();
        var t = NatsJSOpsDefaults.SetTimeouts();
        Assert.Equal(1_000, m.MaxMsgs);
        Assert.Equal(0, m.MaxBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Expires);
        Assert.Equal(TimeSpan.FromSeconds(15), t.IdleHeartbeat);
        Assert.Equal(500, m.ThresholdMsgs);
        Assert.Equal(0, m.ThresholdBytes);
    }

    [Fact]
    public void Allow_only_max_msgs_or_bytes_options() =>
        Assert.Throws<NatsJSException>(() => _ = NatsJSOpsDefaults.SetMax(new NatsJSOpts(NatsOpts.Default), 1, 1));

    [Fact]
    public void Set_bytes_option()
    {
        var opts = NatsJSOpsDefaults.SetMax(new NatsJSOpts(NatsOpts.Default), maxBytes: 1024);
        Assert.Equal(1_000_000, opts.MaxMsgs);
        Assert.Equal(1024, opts.MaxBytes);
        Assert.Equal(500_000, opts.ThresholdMsgs);
        Assert.Equal(512, opts.ThresholdBytes);
    }

    [Fact]
    public void Set_msgs_option()
    {
        var opts = NatsJSOpsDefaults.SetMax(maxMsgs: 10_000);
        Assert.Equal(10_000, opts.MaxMsgs);
        Assert.Equal(0, opts.MaxBytes);
        Assert.Equal(5_000, opts.ThresholdMsgs);
        Assert.Equal(0, opts.ThresholdBytes);
    }

    [Fact]
    public void Set_idle_heartbeat_within_limits_option()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            NatsJSOpsDefaults.SetTimeouts(idleHeartbeat: TimeSpan.FromSeconds(10)).IdleHeartbeat);

        Assert.Equal(
            TimeSpan.FromSeconds(.5),
            NatsJSOpsDefaults.SetTimeouts(idleHeartbeat: TimeSpan.FromSeconds(.1)).IdleHeartbeat);

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            NatsJSOpsDefaults.SetTimeouts(idleHeartbeat: TimeSpan.FromSeconds(60)).IdleHeartbeat);
    }

    [Fact]
    public void Set_idle_expires_within_limits_option()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            NatsJSOpsDefaults.SetTimeouts(expires: TimeSpan.FromSeconds(10)).Expires);

        Assert.Equal(
            TimeSpan.FromSeconds(1),
            NatsJSOpsDefaults.SetTimeouts(expires: TimeSpan.FromSeconds(.1)).Expires);

        Assert.Equal(
            TimeSpan.FromSeconds(300),
            NatsJSOpsDefaults.SetTimeouts(expires: TimeSpan.FromSeconds(300)).Expires);
    }

    [Theory]
    [InlineData(10, 1, 1)]
    [InlineData(10, null, 5)]
    [InlineData(10, 100, 10)]
    public void Set_threshold_option(int max, int? threshold, int expected)
    {
        // Msgs
        {
            var opts = NatsJSOpsDefaults.SetMax(maxMsgs: max, thresholdMsgs: threshold);
            Assert.Equal(expected, opts.ThresholdMsgs);
        }

        // Bytes
        {
            var opts = NatsJSOpsDefaults.SetMax(maxBytes: max, thresholdBytes: threshold);
            Assert.Equal(expected, opts.ThresholdBytes);
        }
    }
}
