using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Tests;

public class ConsumerStateTest
{
    [Fact]
    public void Default_options()
    {
        var m = NatsJSOpsDefaults.SetMax(1_000);
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
        Assert.Throws<NatsJSException>(() => _ = NatsJSOpsDefaults.SetMax(1, 1));

    [Fact]
    public void Set_bytes_option()
    {
        var opts = NatsJSOpsDefaults.SetMax(maxBytes: 1024);
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

        Assert.Throws<NatsJSException>(() => NatsJSOpsDefaults.SetTimeouts(idleHeartbeat: TimeSpan.FromSeconds(.1)));

        Assert.Throws<NatsJSException>(() => NatsJSOpsDefaults.SetTimeouts(idleHeartbeat: TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Set_idle_expires_within_limits_option()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            NatsJSOpsDefaults.SetTimeouts(expires: TimeSpan.FromSeconds(10)).Expires);

        Assert.Throws<NatsJSException>(() => NatsJSOpsDefaults.SetTimeouts(expires: TimeSpan.FromSeconds(.1)));

        Assert.Throws<NatsJSException>(() => NatsJSOpsDefaults.SetTimeouts(expires: TimeSpan.FromSeconds(300)));
    }

    [Theory]
    [InlineData(10, 1, 1, false)]
    [InlineData(10, null, 5, false)]
    [InlineData(10, 100, 10, true)]
    public void Set_threshold_option(int max, int? threshold, int expected, bool invalid)
    {
        // Msgs
        if (!invalid)
        {
            var opts = NatsJSOpsDefaults.SetMax(maxMsgs: max, thresholdMsgs: threshold);
            Assert.Equal(expected, opts.ThresholdMsgs);
        }
        else
        {
            Assert.Throws<NatsJSException>(() => NatsJSOpsDefaults.SetMax(maxMsgs: max, thresholdMsgs: threshold));
        }

        // Bytes
        if (!invalid)
        {
            var opts = NatsJSOpsDefaults.SetMax(maxBytes: max, thresholdBytes: threshold);
            Assert.Equal(expected, opts.ThresholdBytes);
        }
        else
        {
            Assert.Throws<NatsJSException>(() => NatsJSOpsDefaults.SetMax(maxBytes: max, thresholdBytes: threshold));
        }
    }
}
