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

    [Fact]
    public void Consumer_info_should_parse_sourcing_consumers()
    {
        // Consumers the server creates to source an interest or work queue stream are
        // marked 'sourcing' and are allowed the flow control ack policy.
        const string json = """
                            {
                              "type": "io.nats.jetstream.api.v1.consumer_info_response",
                              "stream_name": "s1",
                              "name": "c1",
                              "created": "2026-08-27T10:00:00Z",
                              "config": {
                                "name": "c1",
                                "deliver_policy": "all",
                                "ack_policy": "flow_control",
                                "replay_policy": "instant",
                                "sourcing": true
                              },
                              "delivered": {"consumer_seq": 0, "stream_seq": 0},
                              "ack_floor": {"consumer_seq": 0, "stream_seq": 0},
                              "num_ack_pending": 0,
                              "num_redelivered": 0,
                              "num_waiting": 0,
                              "num_pending": 0
                            }
                            """;

        var serializer = NatsJSJsonSerializer<ConsumerInfoResponse>.Default;
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)));

        Assert.NotNull(result);
        Assert.True(result.Config.Sourcing);
        Assert.False(result.Config.Direct);
        Assert.Equal(ConsumerConfigAckPolicy.FlowControl, result.Config.AckPolicy);
    }

    [Fact]
    public void Consumer_config_should_omit_sourcing_when_not_set()
    {
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig());
        Assert.DoesNotContain("sourcing", Encoding.UTF8.GetString(bw.WrittenSpan.ToArray()));

        bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { Sourcing = true });
        Assert.Contains("\"sourcing\":true", Encoding.UTF8.GetString(bw.WrittenSpan.ToArray()));
    }

    [Fact]
    public void Stream_list_response_should_parse_offline_streams()
    {
        // A stream that needs a higher API level than the server has is reported in
        // 'offline' rather than 'streams', and its name is repeated in 'missing'.
        const string json = """
                            {
                              "type": "io.nats.jetstream.api.v1.stream_list_response",
                              "total": 0,
                              "offset": 0,
                              "limit": 256,
                              "streams": [],
                              "missing": ["s1"],
                              "offline": {"s1": "unsupported required api level 99, server supports 2"}
                            }
                            """;

        var serializer = NatsJSJsonSerializer<StreamListResponse>.Default;
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)));

        Assert.NotNull(result);
        Assert.Empty(result.Streams);
        Assert.Equal(["s1"], result.Missing);
        Assert.NotNull(result.Offline);
        Assert.Equal("unsupported required api level 99, server supports 2", result.Offline["s1"]);
    }

    [Fact]
    public void Consumer_list_response_should_parse_offline_consumers()
    {
        const string json = """
                            {
                              "type": "io.nats.jetstream.api.v1.consumer_list_response",
                              "total": 0,
                              "offset": 0,
                              "limit": 256,
                              "consumers": [],
                              "missing": ["c1"],
                              "offline": {"c1": "unsupported required api level 99, server supports 2"}
                            }
                            """;

        var serializer = NatsJSJsonSerializer<ConsumerListResponse>.Default;
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json)));

        Assert.NotNull(result);
        Assert.Empty(result.Consumers);
        Assert.Equal(["c1"], result.Missing);
        Assert.NotNull(result.Offline);
        Assert.Equal("unsupported required api level 99, server supports 2", result.Offline["c1"]);
    }
}
