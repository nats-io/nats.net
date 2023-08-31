using System.Buffers;
using System.Text;

namespace NATS.Client.Core.Tests;

public class NatsHeaderTest
{
    private readonly ITestOutputHelper _output;

    public NatsHeaderTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public void WriterTests()
    {
        var headers = new NatsHeaders
        {
            ["k1"] = "v1",
            ["k2"] = new[] { "v2-0", "v2-1" },
            ["a-long-header-key"] = "value",
            ["key"] = "a-long-header-value",
        };
        var writer = new HeaderWriter(Encoding.UTF8);
        var buffer = new FixedArrayBufferWriter();
        var written = writer.Write(buffer, headers);

        var text = "k1: v1\r\nk2: v2-0\r\nk2: v2-1\r\na-long-header-key: value\r\nkey: a-long-header-value\r\n\r\n";
        var expected = new Span<byte>(Encoding.UTF8.GetBytes(text));

        Assert.Equal(expected.Length, written);
        Assert.True(expected.SequenceEqual(buffer.WrittenSpan));
        _output.WriteLine($"Buffer:\n{buffer.WrittenSpan.Dump()}");
    }

    [Fact]
    public void ParserTests()
    {
        var parser = new NatsHeaderParser(Encoding.UTF8);
        var text = "NATS/1.0 123 Test Message\r\nk1: v1\r\nk2: v2-0\r\nk2: v2-1\r\na-long-header-key: value\r\nkey: a-long-header-value\r\n\r\n";
        var input = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text)));
        var headers = new NatsHeaders();
        parser.ParseHeaders(input, headers);

        _output.WriteLine($"Headers:\n{headers.Dump()}");

        Assert.Equal(1, headers.Version);
        Assert.Equal(123, headers.Code);
        Assert.Equal("Test Message", headers.MessageText);

        Assert.Equal(4, headers.Count);

        Assert.True(headers.ContainsKey("k1"));
        Assert.Single(headers["k1"].ToArray());
        Assert.Equal("v1", headers["k1"]);

        Assert.True(headers.ContainsKey("k2"));
        Assert.Equal(2, headers["k2"].ToArray().Length);
        Assert.Equal("v2-0", headers["k2"][0]);
        Assert.Equal("v2-1", headers["k2"][1]);

        Assert.True(headers.ContainsKey("a-long-header-key"));
        Assert.Single(headers["a-long-header-key"].ToArray());
        Assert.Equal("value", headers["a-long-header-key"]);

        Assert.True(headers.ContainsKey("key"));
        Assert.Single(headers["key"].ToArray());
        Assert.Equal("a-long-header-value", headers["key"]);
    }

    [Theory]
    [InlineData("Idle Heartbeat", NatsHeaders.Messages.IdleHeartbeat)]
    [InlineData("Bad Request", NatsHeaders.Messages.BadRequest)]
    [InlineData("Consumer Deleted", NatsHeaders.Messages.ConsumerDeleted)]
    [InlineData("Consumer is push based", NatsHeaders.Messages.ConsumerIsPushBased)]
    [InlineData("No Messages", NatsHeaders.Messages.NoMessages)]
    [InlineData("Request Timeout", NatsHeaders.Messages.RequestTimeout)]
    [InlineData("Message Size Exceeds MaxBytes", NatsHeaders.Messages.MessageSizeExceedsMaxBytes)]
    [InlineData("test message", NatsHeaders.Messages.Text)]
    public void ParserMessageEnumTests(string message, NatsHeaders.Messages result)
    {
        var parser = new NatsHeaderParser(Encoding.UTF8);
        var text = $"NATS/1.0 100 {message}\r\n\r\n";
        var input = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text)));
        var headers = new NatsHeaders();
        parser.ParseHeaders(input, headers);
        Assert.Equal(result, headers.Message);
        Assert.Equal(message, headers.MessageText);
    }

    [Fact]
    public void ParserVersionErrorTests()
    {
        var exception = Assert.Throws<NatsException>(() =>
        {
            var parser = new NatsHeaderParser(Encoding.UTF8);
            var text = "NATS/2.0\r\n\r\n";
            var input = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text)));
            var headers = new NatsHeaders();
            parser.ParseHeaders(input, headers);
        });
        Assert.Equal("Protocol error: header version mismatch", exception.Message);
    }

    [Fact]
    public void ParserCodeErrorTests()
    {
        var exception = Assert.Throws<NatsException>(() =>
        {
            var parser = new NatsHeaderParser(Encoding.UTF8);
            var text = "NATS/1.0 x\r\n\r\n";
            var input = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text)));
            var headers = new NatsHeaders();
            parser.ParseHeaders(input, headers);
        });
        Assert.Equal("Protocol error: header code is not a number", exception.Message);
    }

    [Theory]
    [InlineData("NATS/1.0\r\n\r\n", 0, "", 0)]
    [InlineData("NATS/1.0\r\nk:v\r\n\r\n", 0, "", 1)]
    [InlineData("NATS/1.0 42\r\n\r\n", 42, "", 0)]
    [InlineData("NATS/1.0 42\r\nk:v\r\n\r\n", 42, "", 1)]
    [InlineData("NATS/1.0 123 test\r\nk:v\r\n\r\n", 123, "test", 1)]
    [InlineData("NATS/1.0 123 test\r\n\r\n", 123, "test", 0)]
    [InlineData("NATS/1.0 456 test 2\r\n\r\n", 456, "test 2", 0)]
    [InlineData("NATS/1.0 123456 test test 3\r\n\r\n", 123456, "test test 3", 0)]
    [InlineData("NATS/1.0 123456 test test 3\r\nk:v\r\n\r\n", 123456, "test test 3", 1)]
    public void ParserHeaderVersionOnlyTests(string text, int code, string message, int headerCount)
    {
        var parser = new NatsHeaderParser(Encoding.UTF8);
        var input = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text)));
        var headers = new NatsHeaders();
        parser.ParseHeaders(input, headers);
        Assert.Equal(1, headers.Version);
        Assert.Equal(code, headers.Code);
        Assert.Equal(NatsHeaders.Messages.Text, headers.Message);
        Assert.Equal(message, headers.MessageText);
        Assert.Equal(headerCount, headers.Count);
    }

    [Fact]
    public void ParserMultiSpanTests()
    {
        const string text1 = "NATS/1.0 123 Test ";
        const string text2 = "Message\r\n\r\n";

        var parser = new NatsHeaderParser(Encoding.UTF8);
        var builder = new SeqeunceBuilder();
        builder.Append(Encoding.UTF8.GetBytes(text1));
        builder.Append(Encoding.UTF8.GetBytes(text2));
        var input = new SequenceReader<byte>(builder.ToReadOnlySequence());

        var headers = new NatsHeaders();
        parser.ParseHeaders(input, headers);

        Assert.Equal(1, headers.Version);
        Assert.Equal(123, headers.Code);
        Assert.Equal("Test Message", headers.MessageText);
    }
}
