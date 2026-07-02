using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace MicroBenchmark;

/// <summary>
/// Benchmarks comparing old (allocating) vs new (zero-alloc) encoding polyfills
/// used in NETSTANDARD builds for ProtocolWriter, HeaderWriter, and ReadProtocolProcessor.
/// </summary>
[MemoryDiagnoser]
public class EncodingPolyfillBench
{
    private const string ShortSubject = "foo.bar.baz";
    private const string MediumSubject = "my-service.requests.user.profile.update";
    private const string LongHeader = "X-Nats-Stream: my-persistent-stream-name-that-is-quite-long";

    private static readonly Encoding Utf8 = Encoding.UTF8;
    private static readonly Encoding Ascii = Encoding.ASCII;

    private byte[] _shortBytes = null!;
    private byte[] _mediumBytes = null!;
    private byte[] _longBytes = null!;
    private byte[] _destBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _shortBytes = Ascii.GetBytes(ShortSubject);
        _mediumBytes = Ascii.GetBytes(MediumSubject);
        _longBytes = Utf8.GetBytes(LongHeader);
        _destBuffer = new byte[256];
    }

    [Benchmark(Description = "GetBytes_Old_Short")]
    public int GetBytes_Old_Short() => GetBytesOld(Ascii, ShortSubject, _destBuffer);

    [Benchmark(Description = "GetBytes_New_Short")]
    public int GetBytes_New_Short() => GetBytesNew(Ascii, ShortSubject, _destBuffer);

    [Benchmark(Description = "GetBytes_Old_Medium")]
    public int GetBytes_Old_Medium() => GetBytesOld(Ascii, MediumSubject, _destBuffer);

    [Benchmark(Description = "GetBytes_New_Medium")]
    public int GetBytes_New_Medium() => GetBytesNew(Ascii, MediumSubject, _destBuffer);

    [Benchmark(Description = "GetBytes_Old_Long")]
    public int GetBytes_Old_Long() => GetBytesOld(Utf8, LongHeader, _destBuffer);

    [Benchmark(Description = "GetBytes_New_Long")]
    public int GetBytes_New_Long() => GetBytesNew(Utf8, LongHeader, _destBuffer);

    [Benchmark(Description = "GetString_Old_Short")]
    public string GetString_Old_Short() => GetStringOld(Ascii, _shortBytes);

    [Benchmark(Description = "GetString_New_Short")]
    public string GetString_New_Short() => GetStringNew(Ascii, _shortBytes);

    [Benchmark(Description = "GetString_Old_Medium")]
    public string GetString_Old_Medium() => GetStringOld(Ascii, _mediumBytes);

    [Benchmark(Description = "GetString_New_Medium")]
    public string GetString_New_Medium() => GetStringNew(Ascii, _mediumBytes);

    [Benchmark(Description = "GetString_Old_Long")]
    public string GetString_Old_Long() => GetStringOld(Utf8, _longBytes);

    [Benchmark(Description = "GetString_New_Long")]
    public string GetString_New_Long() => GetStringNew(Utf8, _longBytes);

    [Benchmark(Description = "GetBytesWriter_Old_Medium")]
    public void GetBytesWriter_Old_Medium()
    {
        var writer = new ArrayBufferWriter<byte>(256);
        GetBytesWriterOld(Utf8, MediumSubject, writer);
    }

    [Benchmark(Description = "GetBytesWriter_New_Medium")]
    public void GetBytesWriter_New_Medium()
    {
        var writer = new ArrayBufferWriter<byte>(256);
        GetBytesWriterNew(Utf8, MediumSubject, writer);
    }

    private static int GetBytesOld(Encoding encoding, string chars, Span<byte> bytes)
    {
        var buffer = encoding.GetBytes(chars);
        buffer.AsSpan().CopyTo(bytes);
        return buffer.Length;
    }

    private static string GetStringOld(Encoding encoding, ReadOnlySpan<byte> buffer)
    {
        return encoding.GetString(buffer.ToArray());
    }

    private static void GetBytesWriterOld(Encoding encoding, string chars, IBufferWriter<byte> bw)
    {
        var buffer = encoding.GetBytes(chars);
        bw.Write(buffer);
    }

    private static unsafe int GetBytesNew(Encoding encoding, string chars, Span<byte> bytes)
    {
        if (chars.Length == 0)
        {
            return 0;
        }

        fixed (char* charPtr = chars)
        {
            fixed (byte* bytePtr = bytes)
            {
                return encoding.GetBytes(charPtr, chars.Length, bytePtr, bytes.Length);
            }
        }
    }

    private static unsafe string GetStringNew(Encoding encoding, ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return string.Empty;
        }

        fixed (byte* ptr = buffer)
        {
            return encoding.GetString(ptr, buffer.Length);
        }
    }

    private static void GetBytesWriterNew(Encoding encoding, string chars, IBufferWriter<byte> bw)
    {
        var byteCount = encoding.GetByteCount(chars);
        var span = bw.GetSpan(byteCount);
#if NET8_0_OR_GREATER
        encoding.GetBytes(chars.AsSpan(), span);
#else
        GetBytesNew(encoding, chars, span);
#endif
        bw.Advance(byteCount);
    }
}
