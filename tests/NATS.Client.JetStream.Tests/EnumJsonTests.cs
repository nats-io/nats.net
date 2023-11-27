using System.Buffers;
using System.Text;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class EnumJsonTests
{
    private readonly ITestOutputHelper _output;

    public EnumJsonTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(ConsumerConfigAckPolicy.All, "\"ack_policy\":\"all\"")]
    [InlineData(ConsumerConfigAckPolicy.Explicit, "\"ack_policy\":\"explicit\"")]
    [InlineData(ConsumerConfigAckPolicy.None, "\"ack_policy\":\"none\"")]
    public void ConsumerConfigAckPolicy_test(ConsumerConfigAckPolicy value, string expected)
    {
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { AckPolicy = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.AckPolicy);
    }

    [Theory]
    [InlineData(ConsumerConfigDeliverPolicy.All, "\"deliver_policy\":\"all\"")]
    [InlineData(ConsumerConfigDeliverPolicy.Last, "\"deliver_policy\":\"last\"")]
    [InlineData(ConsumerConfigDeliverPolicy.New, "\"deliver_policy\":\"new\"")]
    [InlineData(ConsumerConfigDeliverPolicy.ByStartSequence, "\"deliver_policy\":\"by_start_sequence\"")]
    [InlineData(ConsumerConfigDeliverPolicy.ByStartTime, "\"deliver_policy\":\"by_start_time\"")]
    [InlineData(ConsumerConfigDeliverPolicy.LastPerSubject, "\"deliver_policy\":\"last_per_subject\"")]
    public void ConsumerConfigDeliverPolicy_test(ConsumerConfigDeliverPolicy value, string expected)
    {
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { DeliverPolicy = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.DeliverPolicy);
    }

    [Theory]
    [InlineData(ConsumerConfigReplayPolicy.Instant, "\"replay_policy\":\"instant\"")]
    [InlineData(ConsumerConfigReplayPolicy.Original, "\"replay_policy\":\"original\"")]
    public void ConsumerConfigReplayPolicy_test(ConsumerConfigReplayPolicy value, string expected)
    {
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { ReplayPolicy = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.ReplayPolicy);
    }

    [Theory]
    [InlineData(StreamConfigCompression.None, "\"compression\":\"none\"")]
    [InlineData(StreamConfigCompression.S2, "\"compression\":\"s2\"")]
    public void StreamConfigCompression_test(StreamConfigCompression value, string expected)
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamConfig { Compression = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        if (value == default)
            Assert.DoesNotContain(expected, json);
        else
            Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.Compression);
    }

    [Theory]
    [InlineData(StreamConfigDiscard.Old, "\"discard\":\"old\"")]
    [InlineData(StreamConfigDiscard.New, "\"discard\":\"new\"")]
    public void StreamConfigDiscard_test(StreamConfigDiscard value, string expected)
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamConfig { Discard = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        if (value == default)
            Assert.DoesNotContain(expected, json);
        else
            Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.Discard);
    }

    [Theory]
    [InlineData(StreamConfigRetention.Limits, "\"retention\":\"limits\"")]
    [InlineData(StreamConfigRetention.Interest, "\"retention\":\"interest\"")]
    [InlineData(StreamConfigRetention.Workqueue, "\"retention\":\"workqueue\"")]
    public void StreamConfigRetention_test(StreamConfigRetention value, string expected)
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamConfig { Retention = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.Retention);
    }

    [Theory]
    [InlineData(StreamConfigStorage.File, "\"storage\":\"file\"")]
    [InlineData(StreamConfigStorage.Memory, "\"storage\":\"memory\"")]
    public void StreamConfigStorage_test(StreamConfigStorage value, string expected)
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamConfig { Storage = value });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.Storage);
    }
}
