namespace NATS.Client.Core.Tests;

public class OptsUrlTests
{
    [Fact]
    public void Default_URL()
    {
        var opts = new NatsConnection().Opts;
        Assert.Equal("nats://localhost:4222", opts.Url);
    }

    [Fact]
    public void Redact_URL_user_password()
    {
        var natsUri = new NatsUri("u:p@host", true);
        Assert.Equal("nats://u:***@host:4222", natsUri.ToString());
        Assert.Equal("u:p", natsUri.Uri.UserInfo);
    }

    [Fact]
    public void Redact_URL_token()
    {
        var natsUri = new NatsUri("t@host", true);
        Assert.Equal("nats://***@host:4222", natsUri.ToString());
        Assert.Equal("t", natsUri.Uri.UserInfo);
    }

    [Theory]
    [InlineData("host1", "nats://host1:4222", null, null, null)]
    [InlineData("host1:1234", "nats://host1:1234", null, null, null)]
    [InlineData("tls://host1", "tls://host1:4222", null, null, null)]
    [InlineData("u:p@host1:1234", "nats://u:***@host1:1234", "u", "p", null)]
    [InlineData("t@host1:1234", "nats://***@host1:1234", null, null, "t")]
    [InlineData("host1,host2", "nats://host1:4222,nats://host2:4222", null, null, null)]
    [InlineData("u:p@host1,host2", "nats://u:***@host1:4222,nats://host2:4222", "u", "p", null)]
    [InlineData("u:p@host1,x@host2", "nats://u:***@host1:4222,nats://***@host2:4222", "u", "p", null)]
    [InlineData("t@host1,x:x@host2", "nats://***@host1:4222,nats://x:***@host2:4222", null, null, "t")]
    [InlineData("u:p@host1,host2,host3", "nats://u:***@host1:4222,nats://host2:4222,nats://host3:4222", "u", "p", null)]
    [InlineData("t@host1,@host2,host3", "nats://***@host1:4222,nats://host2:4222,nats://host3:4222", null, null, "t")]
    public void URL_parts(string url, string expected, string? user, string? pass, string? token)
    {
        var opts = new NatsConnection(new NatsOpts { Url = url }).Opts;
        Assert.Equal(expected, GetUrisAsRedactedString(opts));
        Assert.Equal(user, opts.AuthOpts.Username);
        Assert.Equal(pass, opts.AuthOpts.Password);
        Assert.Equal(token, opts.AuthOpts.Token);
    }

    [Theory]
    [InlineData("u:p@host1:1234", "nats://u:***@host1:1234")]
    [InlineData("t@host1:1234", "nats://***@host1:1234")]
    public void URL_should_not_override_auth_options(string url, string expected)
    {
        var opts = new NatsConnection(new NatsOpts
        {
            Url = url,
            AuthOpts = new NatsAuthOpts
            {
                Username = "shouldn't override username",
                Password = "shouldn't override password",
                Token = "shouldn't override token",
            },
        }).Opts;
        Assert.Equal(expected, GetUrisAsRedactedString(opts));
        Assert.Equal("shouldn't override username", opts.AuthOpts.Username);
        Assert.Equal("shouldn't override password", opts.AuthOpts.Password);
        Assert.Equal("shouldn't override token", opts.AuthOpts.Token);
    }

    [Fact]
    public void URL_escape_user_password()
    {
        var opts = new NatsConnection(new NatsOpts { Url = "nats://u%2C:p%2C@host1,host2" }).Opts;
        Assert.Equal("nats://u%2C:***@host1:4222,nats://host2:4222", GetUrisAsRedactedString(opts));
        Assert.Equal("u,", opts.AuthOpts.Username);
        Assert.Equal("p,", opts.AuthOpts.Password);
        Assert.Null(opts.AuthOpts.Token);

        var uris = opts.GetSeedUris(true);
        uris[0].Uri.Scheme.Should().Be("nats");
        uris[0].Uri.Host.Should().Be("host1");
        uris[0].Uri.Port.Should().Be(4222);
        uris[0].Uri.UserInfo.Should().Be("u%2C:p%2C");
        uris[1].Uri.Scheme.Should().Be("nats");
        uris[1].Uri.Host.Should().Be("host2");
        uris[1].Uri.Port.Should().Be(4222);
        uris[1].Uri.UserInfo.Should().Be(string.Empty);
    }

    [Fact]
    public void URL_escape_token()
    {
        var opts = new NatsConnection(new NatsOpts { Url = "nats://t%2C@host1,nats://t%2C@host2" }).Opts;
        Assert.Equal("nats://***@host1:4222,nats://***@host2:4222", GetUrisAsRedactedString(opts));
        Assert.Null(opts.AuthOpts.Username);
        Assert.Null(opts.AuthOpts.Password);
        Assert.Equal("t,", opts.AuthOpts.Token);

        var uris = opts.GetSeedUris(true);
        uris[0].Uri.Scheme.Should().Be("nats");
        uris[0].Uri.Host.Should().Be("host1");
        uris[0].Uri.Port.Should().Be(4222);
        uris[0].Uri.UserInfo.Should().Be("t%2C");
        uris[1].Uri.Scheme.Should().Be("nats");
        uris[1].Uri.Host.Should().Be("host2");
        uris[1].Uri.Port.Should().Be(4222);
        uris[1].Uri.UserInfo.Should().Be("t%2C");
    }

    [Fact]
    public void Keep_URL_wss_path_and_query_string()
    {
        var opts = new NatsConnection(new NatsOpts { Url = "wss://t%2C@host1/path1/path2?q1=1" }).Opts;
        Assert.Equal("wss://***@host1/path1/path2?q1=1", GetUrisAsRedactedString(opts));
        Assert.Null(opts.AuthOpts.Username);
        Assert.Null(opts.AuthOpts.Password);
        Assert.Equal("t,", opts.AuthOpts.Token);
    }

    [Fact]
    public void ToString_reflects_uri_changed_via_with_expression()
    {
        var original = new NatsUri("host1:4222", true);
        Assert.Equal("nats://host1:4222", original.ToString());

        var modified = original with { Uri = new UriBuilder(original.Uri) { Host = "host2", Port = 5222 }.Uri };
        Assert.Equal("nats://host2:5222", modified.ToString());
    }

    [Fact]
    public void ToString_reflects_uri_changed_via_with_expression_redacted()
    {
        var original = new NatsUri("u:p@host1:4222", true);
        Assert.Equal("nats://u:***@host1:4222", original.ToString());

        var modified = original with { Uri = new UriBuilder(original.Uri) { Host = "host2", Port = 5222 }.Uri };
        Assert.Equal("nats://u:***@host2:5222", modified.ToString());
    }

    private static string GetUrisAsRedactedString(NatsOpts opts) => string.Join(",", opts.GetSeedUris(true).Select(u => u.ToString()));
}
