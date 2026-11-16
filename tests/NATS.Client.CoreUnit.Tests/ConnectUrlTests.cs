namespace NATS.Client.Core.Tests;

/// <summary>
/// Servers learned from INFO connect_urls: an entry is host[:port] and nothing else. The scheme,
/// which picks the transport and the TLS mode, is the client's own.
/// </summary>
public class ConnectUrlTests
{
    [Theory]
    [InlineData("127.0.0.1:4222", "tls", "tls://127.0.0.1:4222")]
    [InlineData("host1:4223", "nats", "nats://host1:4223")]
    [InlineData("host1", "tls", "tls://host1:4222")]
    [InlineData("my-host_1.example.com:4222", "tls", "tls://my-host_1.example.com:4222")]
    [InlineData("[::1]:4222", "tls", "tls://[::1]:4222")]
    [InlineData("[::1]", "nats", "nats://[::1]:4222")]
    [InlineData("127.0.0.1:8080", "wss", "wss://127.0.0.1:8080")]
    [InlineData("127.0.0.1:8080", "ws", "ws://127.0.0.1:8080")]

    // port range, leading zeros
    [InlineData("host1:1", "tls", "tls://host1:1")]
    [InlineData("host1:65535", "tls", "tls://host1:65535")]
    [InlineData("host1:00080", "tls", "tls://host1:80")]

    // host forms
    [InlineData("HOST1:4222", "tls", "tls://host1:4222")]
    [InlineData("host.:4222", "tls", "tls://host.:4222")]
    [InlineData("[::ffff:1.2.3.4]:4222", "tls", "tls://[::ffff:1.2.3.4]:4222")]
    [InlineData("[::FFFF]:4222", "tls", "tls://[::ffff]:4222")]

    // numbers that some parsers read as an IPv4 address stay a name
    [InlineData("2130706433:4222", "tls", "tls://2130706433:4222")]
    [InlineData("0x7f.1:4222", "tls", "tls://0x7f.1:4222")]
    [InlineData("127.1:4222", "nats", "nats://127.1:4222")]
    public void Host_and_port_get_the_clients_scheme(string connectUrl, string scheme, string expected)
    {
        var uri = NatsUri.FromConnectUrl(connectUrl, scheme);

        uri.ToString().Should().Be(expected);
        uri.Uri.Scheme.Should().Be(scheme);
        uri.Uri.UserInfo.Should().BeEmpty();
        uri.IsSeed.Should().BeFalse();
    }

    [Theory]

    // a scheme: the server doesn't get to choose the transport or turn TLS off
    [InlineData("ws://127.0.0.1:8080")]
    [InlineData("nats://127.0.0.1:4222")]
    [InlineData("NATS://127.0.0.1:4222")]
    [InlineData("tls://127.0.0.1:4222")]
    [InlineData("\u0001ws://127.0.0.1:8080")]
    [InlineData("na\tts://127.0.0.1:4222")]

    // userinfo: what precedes '@' would be dropped and the rest used as the host
    [InlineData("nats://evil:secret@127.0.0.1:4222")]
    [InlineData("evil@127.0.0.1:4222")]
    [InlineData("10.9.8.7:4333@evil.example:1")]

    // path, query, fragment, whitespace and control characters
    [InlineData("127.0.0.1:8080/x")]
    [InlineData("127.0.0.1:8080?y=1")]
    [InlineData("127.0.0.1:8080#z")]
    [InlineData("127.0.0.1\\x:8080")]
    [InlineData(" 127.0.0.1:4222")]
    [InlineData("127.0.0.1:4222 ")]
    [InlineData("127.0.0.1:4222\r\n")]
    [InlineData("a b:4222")]
    [InlineData("\u0000:1")]

    // not host[:port]
    [InlineData("")]
    [InlineData(":4222")]
    [InlineData("host1:")]
    [InlineData("host1:notaport")]
    [InlineData("host1:12a")]
    [InlineData("host1:0")]
    [InlineData("host1:99999")]
    [InlineData("host1:65536")]
    [InlineData("host1:000080")]
    [InlineData("host1:+80")]
    [InlineData("host1:-1")]
    [InlineData("[::1]:")]
    [InlineData("a..b:4222")]
    [InlineData(".:4222")]
    [InlineData("[1]:4222")]
    [InlineData("[1.2.3.4]:4222")]
    [InlineData("::1:4222")]
    [InlineData("::::")]
    [InlineData("[::1")]
    [InlineData("[]:4222")]
    [InlineData("[::1]4222")]
    [InlineData("[::1]:4222x")]
    [InlineData("%zz:4222")]
    [InlineData("h\u00f6st:4222")]
    [InlineData("\uff45vil.example:4222")]

    // an IPv6 zone id can't be carried through to the dial, so such an entry is refused
    [InlineData("[fe80::1%25lo0]:4222")]
    [InlineData("[fe80::1%lo0]:4222")]
    public void Anything_else_is_refused(string connectUrl)
    {
        var parse = () => NatsUri.FromConnectUrl(connectUrl, "tls");

        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Overlong_host_is_refused()
    {
        var parse = () => NatsUri.FromConnectUrl(new string('a', 300) + ":4222", "tls");

        parse.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("host1", "nats", 4222)]
    [InlineData("host1", "tls", 4222)]
    [InlineData("[::1]", "tls", 4222)]
    [InlineData("host1", "ws", 80)]
    [InlineData("host1", "wss", 443)]
    [InlineData("[::1]", "wss", 443)]
    public void Entry_without_a_port_gets_the_default_port_of_the_scheme(string connectUrl, string scheme, int port)
    {
        var uri = NatsUri.FromConnectUrl(connectUrl, scheme);

        uri.Port.Should().Be(port);
    }

    // For ws:// and wss:// the URL parser reads these as IPv4 addresses, as a browser would.
    [Theory]
    [InlineData("2130706433:8080", "wss", "127.0.0.1")]
    [InlineData("0x7f.1:8080", "wss", "127.0.0.1")]
    [InlineData("127.1:8080", "ws", "127.0.0.1")]
    [InlineData("1:8080", "ws", "0.0.0.1")]
    public void Websocket_scheme_reads_a_numeric_host_as_an_ipv4_address(string connectUrl, string scheme, string host)
    {
        var uri = NatsUri.FromConnectUrl(connectUrl, scheme);

        uri.Host.Should().Be(host);
        uri.Port.Should().Be(8080);
        uri.Uri.UserInfo.Should().BeEmpty();
    }
}
