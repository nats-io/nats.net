using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class CommandConstantsBench
{
    private const ushort NewLineConst = '\r' + ('\n' << 4);
    private const uint PubSpaceConst = 'P' + ('U' << 8) + ('B' << 16) + (' ' << 24);
    private const ulong ConnectSpaceConst = 'C' + ('O' << 8) + ('N' << 16) + ('N' << 24) + ((ulong)'E' << 32) + ((ulong)'C' << 40) + ((ulong)'T' << 48) + ((ulong)' ' << 56);

    private static readonly byte[] Bytes = new byte[8];
    private static readonly ushort NewLineReadonly = BinaryPrimitives.ReadUInt16LittleEndian("\r\n"u8);
    private static readonly uint PubSpaceReadonly = BinaryPrimitives.ReadUInt32LittleEndian("PUB "u8);
    private static readonly ulong ConnectSpaceReadonly = BinaryPrimitives.ReadUInt64LittleEndian("CONNECT "u8);

    [Params(1_000_000)]
    public int Iter { get; set; }

    [Params(2, 4, 8)]
    public int Size { get; set; }

    private static ReadOnlySpan<byte> NewLine => "\r\n"u8;

    private static ReadOnlySpan<byte> PubSpace => "PUB "u8;

    private static ReadOnlySpan<byte> ConnectSpace => "CONNECT "u8;

    [Benchmark]
    public void PubCopy()
    {
        var dest = Bytes.AsSpan();
        switch (Size)
        {
        case 2:
            for (var i = 0; i < Iter; i++)
            {
                NewLine.CopyTo(dest);
            }

            break;
        case 4:
            for (var i = 0; i < Iter; i++)
            {
                PubSpace.CopyTo(dest);
            }

            break;
        case 8:
            for (var i = 0; i < Iter; i++)
            {
                ConnectSpace.CopyTo(dest);
            }

            break;
        }
    }

    [Benchmark]
    public void PubSetIndex()
    {
        var dest = Bytes.AsSpan();
        switch (Size)
        {
        case 2:
            for (var i = 0; i < Iter; i++)
            {
                dest[0] = (byte)'\r';
                dest[1] = (byte)'\n';
            }

            break;
        case 4:
            for (var i = 0; i < Iter; i++)
            {
                dest[0] = (byte)'P';
                dest[1] = (byte)'U';
                dest[2] = (byte)'B';
                dest[3] = (byte)' ';
            }

            break;
        case 8:
            for (var i = 0; i < Iter; i++)
            {
                dest[0] = (byte)'C';
                dest[1] = (byte)'O';
                dest[2] = (byte)'N';
                dest[3] = (byte)'N';
                dest[4] = (byte)'E';
                dest[5] = (byte)'C';
                dest[6] = (byte)'T';
                dest[7] = (byte)' ';
            }

            break;
        }
    }

    [Benchmark]
    public void PubBinaryConst()
    {
        var dest = Bytes.AsSpan();
        switch (Size)
        {
        case 2:
            for (var i = 0; i < Iter; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(dest, NewLineConst);
            }

            break;
        case 4:
            for (var i = 0; i < Iter; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(dest, PubSpaceConst);
            }

            break;
        case 8:
            for (var i = 0; i < Iter; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(dest, ConnectSpaceConst);
            }

            break;
        }
    }

    [Benchmark]
    public void PubBinaryReadonly()
    {
        var dest = Bytes.AsSpan();
        switch (Size)
        {
        case 2:
            for (var i = 0; i < Iter; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(dest, NewLineReadonly);
            }

            break;
        case 4:
            for (var i = 0; i < Iter; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(dest, PubSpaceReadonly);
            }

            break;
        case 8:
            for (var i = 0; i < Iter; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(dest, ConnectSpaceReadonly);
            }

            break;
        }
    }
}
