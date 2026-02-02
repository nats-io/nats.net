using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NATS.Client.Core;
using NATS.Client.Core.Tests;
using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;
using NATS.Client.TestUtilities2;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class TimeSpanJsonTests
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public TimeSpanJsonTests(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [Fact]
    public void NatsJSJsonDateTimeOffsetConverter_serialize_UTC_offset_as_Z()
    {
        var streamSource = new StreamSource
        {
            Name = "events",
            OptStartTime = DateTimeOffset.Parse("2024-01-01T00:00:00+00:00"),
        };

        var json = JsonSerializer.Serialize(streamSource, NatsJSJsonSerializerContext.Default.StreamSource);

        Assert.Equal("""{"name":"events","opt_start_time":"2024-01-01T00:00:00Z"}""", json);
    }

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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(time, result.Active);
    }

    [Fact]
    public void StreamSourceInfoActive_minus_one_indicates_no_activity()
    {
        // When NATS server returns -1 for Active, it means there has been no activity
        // This should be distinguishable from a genuine zero value
        var serializer = NatsJSJsonSerializer<StreamSourceInfo>.Default;

        var jsonWithMinusOne = """{"name":"test","lag":0,"active":-1}"""u8;
        var resultMinusOne = serializer.Deserialize(new ReadOnlySequence<byte>(jsonWithMinusOne.ToArray()));
        Assert.NotNull(resultMinusOne);

        var jsonWithZero = """{"name":"test","lag":0,"active":0}"""u8;
        var resultZero = serializer.Deserialize(new ReadOnlySequence<byte>(jsonWithZero.ToArray()));
        Assert.NotNull(resultZero);

        // These should be different, but currently both become TimeSpan.Zero
        Assert.NotEqual(resultZero.Active, resultMinusOne.Active);
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

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
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

    [Theory]
    [ClassData(typeof(BackoffTestData))]
    public void ConsumerConfigBackoff_test(int minimumFrameworkVersion, List<TimeSpan>? timeSpans, string expected)
    {
        var version = int.Parse(Regex.Match(RuntimeInformation.FrameworkDescription, @"(\d+)\.\d").Groups[1].Value);
        Assert.SkipUnless(version >= minimumFrameworkVersion, $"Requires .NET {minimumFrameworkVersion}");

        var serializer = NatsJSJsonSerializer<ConsumerConfig>.Default;

        var bw = new NatsBufferWriter<byte>();
        serializer.Serialize(bw, new ConsumerConfig { Backoff = timeSpans });

        var json = Encoding.UTF8.GetString(bw.WrittenSpan.ToArray());
        Assert.Matches(expected, json);

        var result = serializer.Deserialize(new ReadOnlySequence<byte>(bw.WrittenMemory));
        Assert.NotNull(result);
        Assert.Equal(timeSpans, result.Backoff);
    }

    [Fact]
    public async Task StreamSourceInfo_active_minus_one_from_real_server()
    {
        // This test demonstrates the bug by connecting to a real NATS server
        // and inspecting the raw JSON response
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        await nats.ConnectRetryAsync();

        var js = new NatsJSContext(nats);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Create source stream (but don't publish any messages to it)
        await js.CreateStreamAsync(
            new StreamConfig($"{prefix}SOURCE", [$"{prefix}foo"]),
            cts.Token);

        // Create mirror stream immediately - no messages have been mirrored yet
        var mirror = await js.CreateStreamAsync(
            new StreamConfig
            {
                Name = $"{prefix}MIRROR",
                Mirror = new StreamSource { Name = $"{prefix}SOURCE" },
            },
            cts.Token);

        // Get the raw JSON response by making the API request directly
        var subject = $"$JS.API.STREAM.INFO.{prefix}MIRROR";
        var reply = await nats.RequestAsync<string, string>(
            subject,
            string.Empty,
            replySerializer: NatsRawSerializer<string>.Default,
            cancellationToken: cts.Token);

        var rawJson = reply.Data;
        Assert.NotNull(rawJson);
        _output.WriteLine("Raw JSON response:");
        _output.WriteLine(rawJson);

        // Verify the raw JSON contains "active":-1 (no activity yet)
        Assert.Contains("\"active\":-1", rawJson);

        // Now check what the deserialized object says
        _output.WriteLine($"Deserialized Mirror.Active: {mirror.Info.Mirror?.Active}");

        // This demonstrates the bug: -1 gets converted to TimeSpan.Zero
        // In the future, this should be null when the property is made nullable
        Assert.Equal(TimeSpan.Zero, mirror.Info.Mirror?.Active);

        // After we fix the bug, this test should be updated to:
        // Assert.Null(mirror.Info.Mirror?.Active);
    }

    private class BackoffTestData : TheoryData<int, List<TimeSpan>?, string>
    {
        public BackoffTestData()
        {
            Add(4, null, "(?!backoff)");
            Add(4, [], "\"backoff\":\\[\\]}");
            Add(4, [TimeSpan.FromMilliseconds(1)], "\"backoff\":\\[1000000\\]}");

            // .NET Framework 4.8 doesn't seeem to support TimeSpan.FromMilliseconds(0.001)
            Add(6, [TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(0.001), TimeSpan.FromMilliseconds(1234)], "\"backoff\":\\[100,1000,1234000000\\]}");

            Add(4, [TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromHours(5)], "\"backoff\":\\[5000000000,300000000000,18000000000000\\]}");
        }
    }
}
