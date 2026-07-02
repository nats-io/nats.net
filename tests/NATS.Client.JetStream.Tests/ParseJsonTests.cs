using System.Buffers;
using System.Text;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class ParseJsonTests
{
    private readonly NatsServerFixture _server;

    public ParseJsonTests(NatsServerFixture server) => _server = server;

    [Fact]
    public void Placement_properties_should_be_optional()
    {
        // This is necessary because when a KV bucket created using nats client, the placement is not set
        // and the server will return an empty object for placement.
        var serializer = NatsJSJsonSerializer<Placement>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new Placement(), default);

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Equal("{}", json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory), default);
        Assert.NotNull(result);
        Assert.Null(result.Cluster);
        Assert.Null(result.Tags);
    }

    [Fact]
    public void Default_consumer_ack_policy_should_be_explicit()
    {
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig(), default);

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Matches("\"ack_policy\":\"explicit\"", json);
    }

    [Fact]
    public void StreamSnapshotRequest_chunk_size_and_window_size_serialization()
    {
        var serializer = NatsJSJsonSerializer<StreamSnapshotRequest>.Default;

        // When not set, chunk_size and window_size should be omitted from JSON
        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamSnapshotRequest { DeliverSubject = "snap" });
        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.DoesNotContain("chunk_size", json);
        Assert.DoesNotContain("window_size", json);
        Assert.Contains("\"deliver_subject\":\"snap\"", json);

        // When set, both should appear with correct values
        bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamSnapshotRequest
        {
            DeliverSubject = "snap",
            ChunkSize = 256 * 1024,
            WindowSize = 16 * 1024 * 1024,
        });
        json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Contains("\"chunk_size\":262144", json);
        Assert.Contains("\"window_size\":16777216", json);

        // Round-trip deserialization
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(256 * 1024, result.ChunkSize);
        Assert.Equal(16 * 1024 * 1024, result.WindowSize);
    }
}
