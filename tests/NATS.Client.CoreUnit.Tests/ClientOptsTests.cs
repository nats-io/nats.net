using System.Text.Json;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Tests;

public class ClientOptsTests
{
    [Fact]
    public void No_responders_is_sent_when_headers_are_enabled()
    {
        var clientOpts = ClientOpts.Create(NatsOpts.Default with { Headers = true });

        clientOpts.Headers.Should().BeTrue();
        clientOpts.NoResponders.Should().BeTrue();
    }

    [Fact]
    public void No_responders_is_not_sent_when_headers_are_disabled()
    {
        var clientOpts = ClientOpts.Create(NatsOpts.Default with { Headers = false });

        clientOpts.Headers.Should().BeFalse();
        clientOpts.NoResponders.Should().BeFalse();

        // The server rejects a CONNECT that asks for no_responders without headers.
        var json = JsonSerializer.Serialize(clientOpts, JsonContext.Default.ClientOpts);
        json.Should().Contain("\"headers\":false");
        json.Should().Contain("\"no_responders\":false");
    }
}
