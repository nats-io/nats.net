using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NATS.Client.Core.Tests;

public class SequenceBuilderTest
{
    [Fact]
    public void AppendOtherBuffer()
    {
        var builder = new SeqeunceBuilder();

        builder.Append(new byte[] { 1, 100, 2 });
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(1);

        builder.Append(new byte[] { 99, 83, 12, 43, 53, 7 });
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2, 99, 83, 12, 43, 53, 7);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(2);

        builder.Append(new byte[] { 103, 34, 16, 9 });
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(3);
    }

    [Fact]
    public void AppendSameBuffer()
    {
        var builder = new SeqeunceBuilder();

        var fullBuffer = new byte[] { 1, 100, 2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9 };

        builder.Append(fullBuffer.AsMemory(0, 3));
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(1);

        builder.Append(fullBuffer.AsMemory(3, 6));
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2, 99, 83, 12, 43, 53, 7);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(1);

        builder.Append(fullBuffer.AsMemory(9));
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(1);

        builder.Append(new byte[] { 44, 33, 22, 11 });
        builder.ToReadOnlySequence().ToArray().ShouldEqual(1, 100, 2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9, 44, 33, 22, 11);
        GetSegmentCount(builder.ToReadOnlySequence()).ShouldBe(2);
    }

    [Fact]
    public void SingleBufferAdvance()
    {
        var builder = new SeqeunceBuilder();
        var fullBuffer = GetMemory(1, 100, 2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9);
        builder.Append(fullBuffer);

        var seq = builder.ToReadOnlySequence();
        builder.Count.Should().Be(13);

        builder.AdvanceTo(seq.GetPosition(2));
        seq = builder.ToReadOnlySequence();
        seq.ToArray().ShouldEqual(2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9);
        builder.Count.Should().Be(11);

        builder.AdvanceTo(seq.GetPosition(5));
        seq = builder.ToReadOnlySequence();
        seq.ToArray().ShouldEqual(53, 7, 103, 34, 16, 9);
        builder.Count.Should().Be(6);

        builder.AdvanceTo(seq.GetPosition(6));
        seq = builder.ToReadOnlySequence();
        seq.ToArray().ShouldEqual();
        builder.Count.Should().Be(0);
    }

    [Fact]
    public void MultipleBufferAdvance()
    {
        var builder = new SeqeunceBuilder();
        builder.Append(GetMemory(1, 100, 2));
        builder.Append(GetMemory(99, 83, 12, 43, 53, 7));
        builder.Append(GetMemory(103, 34, 16, 9));

        var seq = builder.ToReadOnlySequence();
        builder.Count.Should().Be(13);

        builder.AdvanceTo(seq.GetPosition(2));
        seq = builder.ToReadOnlySequence();
        seq.ToArray().ShouldEqual(2, 99, 83, 12, 43, 53, 7, 103, 34, 16, 9);
        builder.Count.Should().Be(11);

        builder.AdvanceTo(seq.GetPosition(5));
        seq = builder.ToReadOnlySequence();
        seq.ToArray().ShouldEqual(53, 7, 103, 34, 16, 9);
        builder.Count.Should().Be(6);

        builder.AdvanceTo(seq.GetPosition(6));
        seq = builder.ToReadOnlySequence();
        seq.ToArray().ShouldEqual();
        builder.Count.Should().Be(0);
    }

    private static int GetSegmentCount(ReadOnlySequence<byte> sequence)
    {
        var count = 0;
        foreach (var x in sequence)
        {
            count++;
        }

        return count;
    }

    private Memory<byte> GetMemory(params byte[] data)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.AsSpan().CopyTo(buffer.AsSpan());
        return buffer.AsMemory(0, data.Length);
    }
}
