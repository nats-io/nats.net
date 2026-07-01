#if NET8_0_OR_GREATER
using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace MicroBenchmark;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class HeaderParserNameEndBench
{
    private const byte ByteColon = (byte)':';
    private const byte ByteSpace = (byte)' ';
    private const byte ByteTab = (byte)'\t';

    private static readonly SearchValues<byte> HeaderNameTerminators = SearchValues.Create([ByteColon, ByteSpace, ByteTab]);
    private static readonly string LongHeader = "X-Nats-Metadata-" + new string('A', 112) + ": value";

    private byte[] _headerLine = null!;

    public enum HeaderLineCase
    {
        ShortValid,
        MediumValid,
        LongValid,
        EmptyName,
        SpaceBeforeColon,
        TabBeforeColon,
        MissingDelimiter,
    }

    [Params(
        HeaderLineCase.ShortValid,
        HeaderLineCase.MediumValid,
        HeaderLineCase.LongValid,
        HeaderLineCase.EmptyName,
        HeaderLineCase.SpaceBeforeColon,
        HeaderLineCase.TabBeforeColon,
        HeaderLineCase.MissingDelimiter)]
    public HeaderLineCase LineCase { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var headerLine = LineCase switch
        {
            HeaderLineCase.ShortValid => "Nats-Msg-Id: value",
            HeaderLineCase.MediumValid => "Nats-Expected-Last-Subject-Sequence: 123",
            HeaderLineCase.LongValid => LongHeader,
            HeaderLineCase.EmptyName => ": value",
            HeaderLineCase.SpaceBeforeColon => "Nats Msg Id: value",
            HeaderLineCase.TabBeforeColon => "Nats\tMsg-Id: value",
            HeaderLineCase.MissingDelimiter => "Nats-Msg-Id",
            _ => throw new ArgumentOutOfRangeException(nameof(LineCase), LineCase, null),
        };

        _headerLine = Encoding.ASCII.GetBytes(headerLine);
    }

    [Benchmark(Baseline = true)]
    public int IndexOfAnyThreeValues() => FindNameEndIndexOfAnyThreeValues(_headerLine);

    [Benchmark]
    public int IndexOfAnySearchValues() => FindNameEndSearchValues(_headerLine);

    private static int FindNameEndIndexOfAnyThreeValues(ReadOnlySpan<byte> headerLine)
    {
        return headerLine.IndexOfAny(ByteColon, ByteSpace, ByteTab);
    }

    private static int FindNameEndSearchValues(ReadOnlySpan<byte> headerLine)
    {
        return headerLine.IndexOfAny(HeaderNameTerminators);
    }
}
#endif
