using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace NATS.Client.JetStream.Tests;

public class ConsumerStateTest
{
    [Fact]
    public void Default_options()
    {
        var opts = new NatsJSConsumeOpts();
        Assert.Equal(1_000, opts.MaxMsgs);
        Assert.Equal(0, opts.MaxBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.Expires);
        Assert.Equal(TimeSpan.FromSeconds(15), opts.IdleHeartbeat);
        Assert.Equal(500, opts.ThresholdMsgs);
        Assert.Equal(0, opts.ThresholdBytes);
    }

    [Fact]
    public void Allow_only_max_msgs_or_bytes_options() =>
        Assert.Throws<NatsJSException>(() => new NatsJSConsumeOpts(maxBytes: 1, maxMsgs: 1));

    [Fact]
    public void Set_bytes_option()
    {
        var opts = new NatsJSConsumeOpts(maxBytes: 1024);
        Assert.Equal(1_000_000, opts.MaxMsgs);
        Assert.Equal(1024, opts.MaxBytes);
        Assert.Equal(500_000, opts.ThresholdMsgs);
        Assert.Equal(512, opts.ThresholdBytes);
    }

    [Fact]
    public void Set_msgs_option()
    {
        var opts = new NatsJSConsumeOpts(maxMsgs: 10_000);
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
            new NatsJSConsumeOpts(idleHeartbeat: TimeSpan.FromSeconds(10)).IdleHeartbeat);

        Assert.Equal(
            TimeSpan.FromSeconds(.5),
            new NatsJSConsumeOpts(idleHeartbeat: TimeSpan.FromSeconds(.1)).IdleHeartbeat);

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            new NatsJSConsumeOpts(idleHeartbeat: TimeSpan.FromSeconds(60)).IdleHeartbeat);
    }

    [Fact]
    public void Set_idle_expires_within_limits_option()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            new NatsJSConsumeOpts(expires: TimeSpan.FromSeconds(10)).Expires);

        Assert.Equal(
            TimeSpan.FromSeconds(1),
            new NatsJSConsumeOpts(expires: TimeSpan.FromSeconds(.1)).Expires);

        Assert.Equal(
            TimeSpan.FromSeconds(300),
            new NatsJSConsumeOpts(expires: TimeSpan.FromSeconds(300)).Expires);
    }

    [Theory]
    [InlineData(10, 1, 1)]
    [InlineData(10, null, 5)]
    [InlineData(10, 100, 10)]
    public void Set_threshold_option(int max, int? threshold, int expected)
    {
        // Msgs
        {
            var opts = new NatsJSConsumeOpts(maxMsgs: max, thresholdMsgs: threshold);
            Assert.Equal(expected, opts.ThresholdMsgs);
        }

        // Bytes
        {
            var opts = new NatsJSConsumeOpts(maxBytes: max, thresholdBytes: threshold);
            Assert.Equal(expected, opts.ThresholdBytes);
        }
    }

    [Fact]
    public void Calculate_pending_msgs()
    {
        var notifications = Channel.CreateUnbounded<int>();
        var pending = new NatsJSConsumer.State<TestData>(new NatsJSConsumeOpts(maxMsgs: 100, thresholdMsgs: 10), notifications, NullLogger.Instance);

        // initial pull
        var init = pending.GetRequest();
        Assert.Equal(100, init.Batch);
        Assert.Equal(0, init.MaxBytes);

        for (var i = 0; i < 89; i++)
        {
            pending.MsgReceived(128);
            Assert.False(pending.CanFetch(), $"iter:{i}");
        }

        pending.MsgReceived(128);
        Assert.True(pending.CanFetch());
        var request = pending.GetRequest();
        Assert.Equal(90, request.Batch);
        Assert.Equal(0, request.MaxBytes);
    }

    [Fact]
    public void Calculate_pending_bytes()
    {
        var notifications = Channel.CreateUnbounded<int>();
        var pending = new NatsJSConsumer.State<TestData>(new NatsJSConsumeOpts(maxBytes: 1000, thresholdBytes: 100), notifications, NullLogger.Instance);

        // initial pull
        var init = pending.GetRequest();
        Assert.Equal(1_000_000, init.Batch);
        Assert.Equal(1000, init.MaxBytes);

        for (var i = 0; i < 89; i++)
        {
            pending.MsgReceived(10);
            Assert.False(pending.CanFetch(), $"iter:{i}");
        }

        pending.MsgReceived(10);
        Assert.True(pending.CanFetch());
        var request = pending.GetRequest();
        Assert.Equal(1_000_000, request.Batch);
        Assert.Equal(900, request.MaxBytes);
    }

    private class TestData
    {
    }
}
