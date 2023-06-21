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

#if DEBUG
        _output.WriteLine($"Buffer:\n{buffer.WrittenSpan.Dump()}");
#endif
    }

    [Fact]
    public void ParserTests()
    {
        var parser = new HeaderParser(Encoding.UTF8);
        var text = "k1: v1\r\nk2: v2-0\r\nk2: v2-1\r\na-long-header-key: value\r\nkey: a-long-header-value\r\n\r\n";
        var input = new SequenceReader<byte>(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text)));
        var headers = new NatsHeaders();
        parser.ParseHeaders(input, headers);

#if DEBUG
        _output.WriteLine($"Headers:\n{headers.Dump()}");
#endif

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
}
