using NATS.Client.JetStream;
using Xunit.Abstractions;

namespace NATS.Client.Compatv1v2;

public class NamespaceTest
{
    private readonly ITestOutputHelper _output;

    public NamespaceTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Namespace_clash_should_not_matter_as_long_as_types_wont_clash()
    {
        var v1 = typeof(StreamState).Assembly.FullName;
        var v2 = typeof(NatsJSContext).Assembly.FullName;

        // v1: NATS.Client, Version=2.4.0.0, Culture=neutral, PublicKeyToken=5b58bc7249e111a9
        // v2: NATS.Client.JetStream, Version=2.4.0.0, Culture=neutral, PublicKeyToken=5b58bc7249e111a9
        _output.WriteLine($"v1: {v1}");
        _output.WriteLine($"v2: {v2}");

        Assert.Contains("NATS.Client,", v1);
        Assert.Contains("NATS.Client.JetStream,", v2);
    }
}
