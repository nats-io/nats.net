namespace NATS.Client.Core.Tests;

public class NatsOptsRequestReplyModeTests
{
    [Fact]
    public void Default_mode_is_direct_but_not_set_intentionally()
    {
        // The default is Direct transport, but because it comes from the field initializer
        // (not the init accessor) it is not flagged as intentional, so no-responders still throws.
        var opts = new NatsOpts();

        Assert.Equal(NatsRequestReplyMode.Direct, opts.RequestReplyMode);
        Assert.False(opts.DirectSetIntentionally);
    }

    [Fact]
    public void Explicit_direct_is_set_intentionally()
    {
        var opts = new NatsOpts { RequestReplyMode = NatsRequestReplyMode.Direct };

        Assert.Equal(NatsRequestReplyMode.Direct, opts.RequestReplyMode);
        Assert.True(opts.DirectSetIntentionally);
    }

    [Fact]
    public void Explicit_shared_inbox_is_not_set_intentionally()
    {
        var opts = new NatsOpts { RequestReplyMode = NatsRequestReplyMode.SharedInbox };

        Assert.Equal(NatsRequestReplyMode.SharedInbox, opts.RequestReplyMode);
        Assert.False(opts.DirectSetIntentionally);
    }

    [Fact]
    public void With_setting_direct_flags_intentional()
    {
        var opts = new NatsOpts() with { RequestReplyMode = NatsRequestReplyMode.Direct };

        Assert.True(opts.DirectSetIntentionally);
    }

    [Fact]
    public void With_not_touching_mode_preserves_default_flag()
    {
        var opts = new NatsOpts() with { Name = "x" };

        Assert.Equal(NatsRequestReplyMode.Direct, opts.RequestReplyMode);
        Assert.False(opts.DirectSetIntentionally);
    }
}
