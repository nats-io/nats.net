using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Tests;

public class FixedArrayBufferWriterTest
{
    [Fact]
    public void Standard()
    {
        var writer = new FixedArrayBufferWriter();

        var buffer = writer.GetSpan();
        buffer[0] = 100;
        buffer[1] = 200;
        buffer[2] = 220;

        writer.Advance(3);

        var buffer2 = writer.GetSpan();
        buffer2[0].Should().Be(0);
        (buffer.Length - buffer2.Length).Should().Be(3);

        buffer2[0] = 244;
        writer.Advance(1);

        writer.WrittenMemory.ToArray().Should().Equal(100, 200, 220, 244);
    }

    [Fact]
    public void Ensure()
    {
        var writer = new FixedArrayBufferWriter();

        writer.Advance(20000);

        var newSpan = writer.GetSpan(50000);

        newSpan.Length.Should().Be((ushort.MaxValue * 2) - 20000);
    }

    [Theory]
    [InlineData(129, 0, "double capacity")]
    [InlineData(257, 0, "adjust capacity to size")]
    [InlineData(129, 1, "double capacity when already advanced")]
    [InlineData(257, 1, "adjust capacity to size when already advanced")]
    public void Resize(int size, int advance, string reason)
    {
        // GetSpan()
        {
            var writer = new FixedArrayBufferWriter(128);
            if (advance > 0)
                writer.Advance(advance);
            var span = writer.GetSpan(size);
            span.Length.Should().BeGreaterOrEqualTo(size, reason);
        }

        // GetMemory()
        {
            var writer = new FixedArrayBufferWriter(128);
            if (advance > 0)
                writer.Advance(advance);
            var memory = writer.GetMemory(size);
            memory.Length.Should().BeGreaterOrEqualTo(size, reason);
        }
    }
}
