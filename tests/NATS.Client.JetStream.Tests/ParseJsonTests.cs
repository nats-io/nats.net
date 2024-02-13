using System.Buffers;
using System.Text;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class ParseJsonTests
{
    [Fact]
    public void Placement_properties_should_be_optional()
    {
        // This is necessary because when a KV bucket created using nats client, the placement is not set
        // and the server will return an empty object for placement.
        var serializer = NatsJSJsonSerializer<Placement>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new Placement());

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Equal("{}", json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Null(result.Cluster);
        Assert.Null(result.Tags);
    }
}
