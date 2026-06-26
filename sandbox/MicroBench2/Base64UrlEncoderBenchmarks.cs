using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.ObjectStore.Internal;

namespace MicroBench2;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net80)]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class Base64UrlEncoderBenchmarks
{
    private const char PadChar = '=';

    private static readonly char[] SBase64Table =
    {
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-', '_',
    };

    private byte[] _data = null!;

    // SHA-256 digest (32) is the common case; the others bracket small and larger inputs.
    [Params(16, 32, 256, 4096)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size];
        for (var i = 0; i < Size; i++)
            _data[i] = (byte)((i * 37) + 11);
    }

    // Baseline: the original lookup-table encoder that allocated a fresh char[] per call.
    [Benchmark(Baseline = true, Description = "table (new char[])")]
    public string EncodeTableNewArray() => EncodeTableNewArrayImpl(_data);

    [Benchmark(Description = "current")]
    public string Encode() => Base64UrlEncoder.Encode(_data);

    private static string EncodeTableNewArrayImpl(Span<byte> inArray)
    {
        var length = inArray.Length;
        if (length == 0)
            return string.Empty;

        var lengthMod3 = length % 3;
        var limit = length - lengthMod3;
        var output = new char[(length + 2) / 3 * 4];
        var table = SBase64Table;
        var j = 0;

        for (var i = 0; i < limit; i += 3)
        {
            var d0 = inArray[i];
            var d1 = inArray[i + 1];
            var d2 = inArray[i + 2];

            output[j + 0] = table[d0 >> 2];
            output[j + 1] = table[((d0 & 0x03) << 4) | (d1 >> 4)];
            output[j + 2] = table[((d1 & 0x0f) << 2) | (d2 >> 6)];
            output[j + 3] = table[d2 & 0x3f];
            j += 4;
        }

        switch (lengthMod3)
        {
        case 2:
            {
                var d0 = inArray[limit];
                var d1 = inArray[limit + 1];
                output[j + 0] = table[d0 >> 2];
                output[j + 1] = table[((d0 & 0x03) << 4) | (d1 >> 4)];
                output[j + 2] = table[(d1 & 0x0f) << 2];
                j += 3;
            }

            break;

        case 1:
            {
                var d0 = inArray[limit];
                output[j + 0] = table[d0 >> 2];
                output[j + 1] = table[(d0 & 0x03) << 4];
                j += 2;
            }

            break;
        }

        for (var k = j; k < output.Length; k++)
            output[k] = PadChar;

        return new string(output);
    }
}
