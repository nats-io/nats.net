namespace NATS.Client.Core.Tests;

public class OptsUrlTests
{
    [Fact]
    public void Default_URL()
    {
        var opts = new NatsConnection().Opts;
        Assert.Equal("nats://localhost:4222", opts.Url);
    }

    [Theory]
    [InlineData("host1", "nats://host1:4222", null, null, null)]
    [InlineData("host1:1234", "nats://host1:1234", null, null, null)]
    [InlineData("tls://host1", "tls://host1:4222", null, null, null)]
    [InlineData("u:p@host1:1234", "nats://u:***@host1:1234", "u", "p", null)]
    [InlineData("t@host1:1234", "nats://***@host1:1234", null, null, "t")]
    [InlineData("host1,host2", "nats://host1:4222,nats://host2:4222", null, null, null)]
    [InlineData("u:p@host1,host2", "nats://u:***@host1:4222,nats://u:***@host2:4222", "u", "p", null)]
    [InlineData("u:p@host1,x@host2", "nats://u:***@host1:4222,nats://u:***@host2:4222", "u", "p", null)]
    [InlineData("t@host1,x:x@host2", "nats://***@host1:4222,nats://***@host2:4222", null, null, "t")]
    [InlineData("u:p@host1,host2,host3", "nats://u:***@host1:4222,nats://u:***@host2:4222,nats://u:***@host3:4222", "u", "p", null)]
    [InlineData("t@host1,@host2,host3", "nats://***@host1:4222,nats://***@host2:4222,nats://***@host3:4222", null, null, "t")]
    public void URL_parts(string url, string expected, string? user, string? pass, string? token)
    {
        var opts = new NatsConnection(new NatsOpts { Url = url }).Opts;
        Assert.Equal(expected, opts.Url);
        Assert.Equal(user, opts.AuthOpts.Username);
        Assert.Equal(pass, opts.AuthOpts.Password);
        Assert.Equal(token, opts.AuthOpts.Token);
    }

    [Theory]
    [InlineData("u:p@host1:1234", "nats://u:***@host1:1234", "u", "p", null)]
    [InlineData("t@host1:1234", "nats://***@host1:1234", null, null, "t")]
    public void URL_should_override_auth_options(string url, string expected, string? user, string? pass, string? token)
    {
        var opts = new NatsConnection(new NatsOpts
        {
            Url = url,
            AuthOpts = new NatsAuthOpts
            {
                Username = "should override username",
                Password = "should override password",
                Token = "should override token",
            },
        }).Opts;
        Assert.Equal(expected, opts.Url);
        Assert.Equal(user, opts.AuthOpts.Username);
        Assert.Equal(pass, opts.AuthOpts.Password);
        Assert.Equal(token, opts.AuthOpts.Token);
    }

    [Fact]
    public void URL_escape_user_password()
    {
        var opts = new NatsConnection(new NatsOpts { Url = "nats://u%2C:p%2C@host1,host2" }).Opts;
        Assert.Equal("nats://u%2C:***@host1:4222,nats://u%2C:***@host2:4222", opts.Url);
        Assert.Equal("u,", opts.AuthOpts.Username);
        Assert.Equal("p,", opts.AuthOpts.Password);
        Assert.Null(opts.AuthOpts.Token);

        var uris = opts.GetSeedUris(true);
        uris[0].Uri.Scheme.Should().Be("nats");
        uris[0].Uri.Host.Should().Be("host1");
        uris[0].Uri.Port.Should().Be(4222);
        uris[0].Uri.UserInfo.Should().Be("u%2C:***");
        uris[1].Uri.Scheme.Should().Be("nats");
        uris[1].Uri.Host.Should().Be("host2");
        uris[1].Uri.Port.Should().Be(4222);
        uris[1].Uri.UserInfo.Should().Be("u%2C:***");
    }

    [Fact]
    public void URL_escape_token()
    {
        var opts = new NatsConnection(new NatsOpts { Url = "nats://t%2C@host1,nats://t%2C@host2" }).Opts;
        Assert.Equal("nats://***@host1:4222,nats://***@host2:4222", opts.Url);
        Assert.Null(opts.AuthOpts.Username);
        Assert.Null(opts.AuthOpts.Password);
        Assert.Equal("t,", opts.AuthOpts.Token);

        var uris = opts.GetSeedUris(true);
        uris[0].Uri.Scheme.Should().Be("nats");
        uris[0].Uri.Host.Should().Be("host1");
        uris[0].Uri.Port.Should().Be(4222);
        uris[0].Uri.UserInfo.Should().Be("***");
        uris[1].Uri.Scheme.Should().Be("nats");
        uris[1].Uri.Host.Should().Be("host2");
        uris[1].Uri.Port.Should().Be(4222);
        uris[1].Uri.UserInfo.Should().Be("***");
    }
}
