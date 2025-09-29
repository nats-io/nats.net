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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.Storage);
    }

    [Theory]
    [InlineData(ConsumerCreateAction.Create, "{\"stream_name\":\"\",\"config\":null,\"action\":\"create\"}")]
    [InlineData(ConsumerCreateAction.Update, "{\"stream_name\":\"\",\"config\":null,\"action\":\"update\"}")]
    [InlineData(ConsumerCreateAction.CreateOrUpdate, "{\"stream_name\":\"\",\"config\":null}")]
    public void ConsumerCreateRequestAction_Test(ConsumerCreateAction value, string expected)
    {
        var serializer = NatsJSJsonSerializer<ConsumerCreateRequest>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerCreateRequest { Action = value, StreamName = string.Empty });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Contains(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.Action);
    }

    [Fact]
    public void StreamConfigPersistMode_null_not_serialized()
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        // When PersistMode is null (not explicitly set), it should not be included in JSON
        var bw = new NatsBufferWriter<byte>();
        var config = new StreamConfig { PersistMode = null };
        serializer.Serialize(bw, config);

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.DoesNotContain("persist_mode", json);

        // Deserialize and verify it remains null
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Null(result.PersistMode);
    }

    [Theory]
    [InlineData(StreamConfigPersistMode.Default, "\"persist_mode\":\"default\"")]
    [InlineData(StreamConfigPersistMode.Async, "\"persist_mode\":\"async\"")]
    public void StreamConfigPersistMode_explicit_value_serialized(StreamConfigPersistMode value, string expected)
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        // When PersistMode is explicitly set (even to Default), it should be included in JSON
        var bw = new NatsBufferWriter<byte>();
        var config = new StreamConfig { PersistMode = value };
        serializer.Serialize(bw, config);

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Contains(expected, json);

        // Deserialize and verify the value is preserved
        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(value, result.PersistMode);
    }

    [Fact]
    public void StreamConfigPersistMode_roundtrip_preserves_server_values()
    {
        var serializer = NatsJSJsonSerializer<StreamConfig>.Default;

        // Test case 1: Server returns "default" - should preserve it
        var jsonWithDefault = "{\"persist_mode\":\"default\",\"retention\":\"limits\",\"storage\":\"file\"}";
        var bytes = Encoding.UTF8.GetBytes(jsonWithDefault);
        var configFromServer = serializer.Deserialize(new ReadOnlySequence<byte>(bytes));
        Assert.NotNull(configFromServer);
        Assert.Equal(StreamConfigPersistMode.Default, configFromServer.PersistMode);

        // When we serialize it again, it should include persist_mode
        var bw1 = new NatsBufferWriter<byte>();
        serializer.Serialize(bw1, configFromServer);
        var jsonOut1 = Encoding.UTF8.GetString(bw1.WrittenSpan.ToArray());
        Assert.Contains("\"persist_mode\":\"default\"", jsonOut1);

        // Test case 2: Server returns "async" - should preserve it
        var jsonWithAsync = "{\"persist_mode\":\"async\",\"retention\":\"limits\",\"storage\":\"file\"}";
        bytes = Encoding.UTF8.GetBytes(jsonWithAsync);
        configFromServer = serializer.Deserialize(new ReadOnlySequence<byte>(bytes));
        Assert.NotNull(configFromServer);
        Assert.Equal(StreamConfigPersistMode.Async, configFromServer.PersistMode);

        // When we serialize it again, it should include persist_mode
        var bw2 = new NatsBufferWriter<byte>();
        serializer.Serialize(bw2, configFromServer);
        var jsonOut2 = Encoding.UTF8.GetString(bw2.WrittenSpan.ToArray());
        Assert.Contains("\"persist_mode\":\"async\"", jsonOut2);

        // Test case 3: Server doesn't return persist_mode - should remain null
        var jsonWithoutPersistMode = "{\"retention\":\"limits\",\"storage\":\"file\"}";
        bytes = Encoding.UTF8.GetBytes(jsonWithoutPersistMode);
        configFromServer = serializer.Deserialize(new ReadOnlySequence<byte>(bytes));
        Assert.NotNull(configFromServer);
        Assert.Null(configFromServer.PersistMode);

        // When we serialize it again, it should NOT include persist_mode
        var bw3 = new NatsBufferWriter<byte>();
        serializer.Serialize(bw3, configFromServer);
        var jsonOut3 = Encoding.UTF8.GetString(bw3.WrittenSpan.ToArray());
        Assert.DoesNotContain("persist_mode", jsonOut3);
    }
}
