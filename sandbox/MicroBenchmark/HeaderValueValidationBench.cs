#if NET8_0_OR_GREATER
using System.Text;
using BenchmarkDotNet.Attributes;

namespace MicroBenchmark;

public class HeaderValueValidationBench
{
    private const byte ByteCr = (byte)'\r';
    private const byte ByteLf = (byte)'\n';

    private byte[] _value = null!;

    public enum HeaderValueCase
    {
        ShortValid,
        LongValid,
        CrAtEnd,
        CrWithoutLf,
        CrLfAtStart,
        CrLfAtEnd,
    }

    [Params(
        HeaderValueCase.ShortValid,
        HeaderValueCase.LongValid,
        HeaderValueCase.CrAtEnd,
        HeaderValueCase.CrWithoutLf,
        HeaderValueCase.CrLfAtStart,
        HeaderValueCase.CrLfAtEnd)]
    public HeaderValueCase ValueCase { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var value = ValueCase switch
        {
            HeaderValueCase.ShortValid => "ok",
            HeaderValueCase.LongValid => new string('A', 256),
            HeaderValueCase.CrAtEnd => "value\r",
            HeaderValueCase.CrWithoutLf => "value\rnext",
            HeaderValueCase.CrLfAtStart => "\r\nvalue",
            HeaderValueCase.CrLfAtEnd => "value\r\n",
            _ => throw new ArgumentOutOfRangeException(nameof(ValueCase), ValueCase, null),
        };

        _value = Encoding.ASCII.GetBytes(value);
    }

    [Benchmark(Baseline = true)]
    public bool IndexOfCrLoop() => ValidateValueIndexOfCrLoop(_value);

    [Benchmark]
    public bool IndexOfCrLfSequence() => ValidateValueIndexOfCrLfSequence(_value);

    private static bool ValidateValueIndexOfCrLoop(ReadOnlySpan<byte> span)
    {
        while (true)
        {
            var pos = span.IndexOf(ByteCr);
            if (pos == -1 || pos == span.Length - 1)
                return true;

            pos += 1;
            if (span[pos] == ByteLf)
                return false;

            span = span[pos..];
        }
    }

    private static bool ValidateValueIndexOfCrLfSequence(ReadOnlySpan<byte> span)
    {
        return span.IndexOf("\r\n"u8) < 0;
    }
}
#endif
