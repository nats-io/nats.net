#if NET8_0_OR_GREATER
using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace MicroBenchmark;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class HeaderKeyValidationBench
{
    private const byte ByteColon = (byte)':';
    private const byte ByteSpace = (byte)' ';
    private const byte ByteDel = 127;

    private static readonly SearchValues<byte> ValidKeyBytes = SearchValues.Create(CreateValidKeyBytes());
    private static readonly SearchValues<byte> InvalidKeyBytes = SearchValues.Create(CreateInvalidKeyBytes());

    private byte[] _key = null!;

    public enum HeaderKeyCase
    {
        ShortValid,
        MediumValid,
        InvalidStart,
        InvalidMiddle,
        InvalidEnd,
        InvalidNonAscii,
    }

    [Params(
        HeaderKeyCase.ShortValid,
        HeaderKeyCase.MediumValid,
        HeaderKeyCase.InvalidStart,
        HeaderKeyCase.InvalidMiddle,
        HeaderKeyCase.InvalidEnd,
        HeaderKeyCase.InvalidNonAscii)]
    public HeaderKeyCase KeyCase { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var key = KeyCase switch
        {
            HeaderKeyCase.ShortValid => "Nats-Msg-Id",
            HeaderKeyCase.MediumValid => "Nats-Expected-Last-Subject-Sequence",
            HeaderKeyCase.InvalidStart => ":Nats-Msg-Id",
            HeaderKeyCase.InvalidMiddle => "Nats Msg Id",
            HeaderKeyCase.InvalidEnd => "Nats-Msg-Id\n",
            HeaderKeyCase.InvalidNonAscii => "Nats-Msg-Id-é",
            _ => throw new ArgumentOutOfRangeException(nameof(KeyCase), KeyCase, null),
        };

        _key = Encoding.UTF8.GetBytes(key);
    }

    [Benchmark(Baseline = true)]
    public bool ByteLoop() => ValidateKeyByteLoop(_key);

    [Benchmark]
    public bool SearchValuesInvalidBytes() => ValidateKeySearchValuesInvalidBytes(_key);

    [Benchmark]
    public bool SearchValuesValidBytes() => ValidateKeySearchValuesValidBytes(_key);

    [Benchmark]
    public bool RangeExceptAndColon() => ValidateKeyRangeExceptAndColon(_key);

    private static bool ValidateKeyByteLoop(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
        {
            if (b <= ByteSpace || b == ByteColon || b >= ByteDel)
                return false;
        }

        return true;
    }

    private static bool ValidateKeySearchValuesInvalidBytes(ReadOnlySpan<byte> span)
    {
        return !span.ContainsAny(InvalidKeyBytes);
    }

    private static bool ValidateKeySearchValuesValidBytes(ReadOnlySpan<byte> span)
    {
        return !span.ContainsAnyExcept(ValidKeyBytes);
    }

    private static bool ValidateKeyRangeExceptAndColon(ReadOnlySpan<byte> span)
    {
        return !span.ContainsAnyExceptInRange((byte)33, (byte)126) && !span.Contains(ByteColon);
    }

    private static byte[] CreateValidKeyBytes()
    {
        var bytes = new byte[ByteDel - ByteSpace - 2];
        var index = 0;

        for (var b = ByteSpace + 1; b < ByteDel; b++)
        {
            if (b != ByteColon)
            {
                bytes[index++] = (byte)b;
            }
        }

        return bytes;
    }

    private static byte[] CreateInvalidKeyBytes()
    {
        var bytes = new byte[(int)ByteSpace + 1 + 1 + byte.MaxValue - ByteDel + 1];
        var index = 0;

        for (var b = 0; b <= ByteSpace; b++)
        {
            bytes[index++] = (byte)b;
        }

        bytes[index++] = ByteColon;

        for (var b = (int)ByteDel; b <= byte.MaxValue; b++)
        {
            bytes[index++] = (byte)b;
        }

        return bytes;
    }
}
#endif
