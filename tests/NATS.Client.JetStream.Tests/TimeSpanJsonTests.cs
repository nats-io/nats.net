using System.Buffers;
using System.Text;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class TimeSpanJsonTests
{
    [Theory]
    [InlineData("00:00:00.001", "\"ack_wait\":1000000\\b")]
    [InlineData("00:00:01.000", "\"ack_wait\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"ack_wait\":1234000000\\b")]
    public void ConsumerConfigAckWait_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { AckWait = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.AckWait);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"idle_heartbeat\":1000000\\b")]
    [InlineData("00:00:01.000", "\"idle_heartbeat\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"idle_heartbeat\":1234000000\\b")]
    public void ConsumerConfigIdleHeartbeat_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { IdleHeartbeat = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.IdleHeartbeat);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"inactive_threshold\":1000000\\b")]
    [InlineData("00:00:01.000", "\"inactive_threshold\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"inactive_threshold\":1234000000\\b")]
    public void ConsumerConfigInactiveThreshold_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { InactiveThreshold = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.InactiveThreshold);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"max_expires\":1000000\\b")]
    [InlineData("00:00:01.000", "\"max_expires\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"max_expires\":1234000000\\b")]
    public void ConsumerConfigMaxExpires_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { MaxExpires = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.MaxExpires);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"expires\":1000000\\b")]
    [InlineData("00:00:01.000", "\"expires\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"expires\":1234000000\\b")]
    public void ConsumerGetnextRequestExpires_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<ConsumerGetnextRequest>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerGetnextRequest { Expires = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.Expires);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"idle_heartbeat\":1000000\\b")]
    [InlineData("00:00:01.000", "\"idle_heartbeat\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"idle_heartbeat\":1234000000\\b")]
    public void ConsumerGetnextRequestIdleHeartbeat_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<ConsumerGetnextRequest>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerGetnextRequest { IdleHeartbeat = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.IdleHeartbeat);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"active\":1000000\\b")]
    [InlineData("00:00:01.000", "\"active\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"active\":1234000000\\b")]
    public void PeerInfoActive_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<PeerInfo>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new PeerInfo { Name = "test", Active = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.Active);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"max_age\":1000000\\b")]
    [InlineData("00:00:01.000", "\"max_age\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"max_age\":1234000000\\b")]
    public void StreamConfigMaxAge_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamConfig { MaxAge = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.MaxAge);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"duplicate_window\":1000000\\b")]
    [InlineData("00:00:01.000", "\"duplicate_window\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"duplicate_window\":1234000000\\b")]
    public void StreamConfigDuplicateWindow_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamConfig { DuplicateWindow = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.DuplicateWindow);
    }

    [Theory]
    [InlineData("00:00:00.001", "\"active\":1000000\\b")]
    [InlineData("00:00:01.000", "\"active\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"active\":1234000000\\b")]
    public void StreamSourceInfoActive_test(string value, string expected)
    {
        var time = TimeSpan.Parse(value);
        var serializer = NatsJSJsonSerializer<StreamSourceInfo>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new StreamSourceInfo { Name = "test", Active = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.Active);
    }

    [Theory]
    [InlineData(null, "\"pause_remaining\":null\\b")]
    [InlineData("00:00:00.001", "\"pause_remaining\":1000000\\b")]
    [InlineData("00:00:01.000", "\"pause_remaining\":1000000000\\b")]
    [InlineData("00:00:01.234", "\"pause_remaining\":1234000000\\b")]
    public void ConsumerInfoPauseRemaining_test(string? value, string expected)
    {
        TimeSpan? time = value != null ? TimeSpan.Parse(value) : null;
        var serializer = NatsJSJsonSerializer<ConsumerInfo>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerInfo { StreamName = "test", Name = "test", PauseRemaining = time });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan);
        if (value != null)
        {
            Assert.Matches(expected, json);
        }
        else
        {
            // PauseRemaining should not be serialized, if the value is null.
            Assert.DoesNotMatch(expected, "pause_remaining");
        }

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.PauseRemaining);
    }
}
