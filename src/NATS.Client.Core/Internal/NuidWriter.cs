using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;

namespace NATS.Client.Core.Internal;

[SkipLocalsInit]
internal sealed class NuidWriter
{
    private const nuint BASE = 62;
    private const ulong MAXSEQUENTIAL = 839299365868340224; // 62^10   // 0x1000_0000_0000_0000; // 64 ^10
    private const uint PREFIXLENGTH = 12;
    private const nuint SEQUENTIALLENGTH = 10;
    private const int MININCREMENT = 33;
    private const int MAXINCREMENT = 333;
    internal const nuint NUIDLENGTH = PREFIXLENGTH + SEQUENTIALLENGTH;

    [ThreadStatic]
    private static NuidWriter? writer;

    // TODO: Use UTF8 string literal when upgrading to .NET 7+
    private static ReadOnlySpan<char> Digits => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private char[] _prefix;
    private ulong _increment;
    private ulong _sequential;

    internal static int PrefixLength => (int)PREFIXLENGTH;

    private NuidWriter()
    {
        Refresh(out _);
    }

    public static bool TryWriteNuid(Span<char> nuidBuffer)
    {
        if (writer is not null)
        {
            return writer.TryWriteNuidCore(nuidBuffer);
        }

        return InitAndWrite(nuidBuffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InitAndWrite(Span<char> span)
    {
        writer = new NuidWriter();
        return writer.TryWriteNuidCore(span);
    }

    private bool TryWriteNuidCore(Span<char> nuidBuffer)
    {
        var sequential = _sequential += _increment;

        if (sequential < MAXSEQUENTIAL)
        {
            return TryWriteNuidCore(nuidBuffer, _prefix, sequential);
        }

        return RefreshAndWrite(nuidBuffer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool RefreshAndWrite(Span<char> buffer)
        {
            var prefix = Refresh(out sequential);
            return TryWriteNuidCore(buffer, prefix, sequential);
        }
    }

    private static bool TryWriteNuidCore(Span<char> buffer, Span<char> prefix, ulong sequential)
    {
        if ((uint)buffer.Length < NUIDLENGTH || prefix.Length != PREFIXLENGTH || (uint)prefix.Length > (uint)buffer.Length)
        {
            return false;
        }

        Unsafe.CopyBlockUnaligned(ref Unsafe.As<char, byte>(ref buffer[0]), ref Unsafe.As<char, byte>(ref prefix[0]), PREFIXLENGTH * sizeof(char));

        // NOTE: We must never write to digitsPtr!
        ref var digitsPtr = ref MemoryMarshal.GetReference(Digits);

        for (nuint i = PREFIXLENGTH; i < NUIDLENGTH; i++)
        {
            var digitIndex = (nuint)(sequential % BASE);
            Unsafe.Add(ref buffer[0], i) = Unsafe.Add(ref digitsPtr, digitIndex);
            sequential /= BASE;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [MemberNotNull(nameof(_prefix))]
    private char[] Refresh(out ulong sequential)
    {
        var prefix = _prefix = GetPrefix();
        _increment = GetIncrement();
        sequential = _sequential = GetSequential();
        return prefix;
    }

    private static uint GetIncrement()
    {
        return (uint)Random.Shared.Next(MININCREMENT, MAXINCREMENT + 1);
    }

    private static ulong GetSequential()
    {
        return (ulong)Random.Shared.NextInt64(0, (long)MAXSEQUENTIAL + 1);
    }

    private static char[] GetPrefix(RandomNumberGenerator? rng = null)
    {
        Span<byte> randomBytes = stackalloc byte[(int)PREFIXLENGTH];

        // TODO: For .NET 8+, use GetItems for better distribution
        if (rng == null)
        {
            RandomNumberGenerator.Fill(randomBytes);
        }
        else
        {
            rng.GetBytes(randomBytes);
        }

        var newPrefix = new char[PREFIXLENGTH];

        for (var i = 0; i < randomBytes.Length; i++)
        {
            var digitIndex = (int)(randomBytes[i] % BASE);
            newPrefix[i] = Digits[digitIndex];
        }

        return newPrefix;
    }
}
