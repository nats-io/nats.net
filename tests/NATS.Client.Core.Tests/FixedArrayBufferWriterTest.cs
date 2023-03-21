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
}
