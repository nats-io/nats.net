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
    private const ulong MAX_SEQUENTIAL = 839299365868340224; // 62^10   // 0x1000_0000_0000_0000; // 64 ^10
    private const uint PREFIX_LENGTH = 12;
    private const nuint SEQUENTIAL_LENGTH = 10;
    private const int MIN_INCREMENT = 33;
    private const int MAX_INCREMENT = 333;
    internal const nuint NUID_LENGTH = PREFIX_LENGTH + SEQUENTIAL_LENGTH;

    [ThreadStatic]
    private static NuidWriter? t_writer;

    // TODO: Use UTF8 string literal when upgrading to .NET 7+
    private static ReadOnlySpan<char> Digits => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private char[] _prefix;
    private ulong _increment;
    private ulong _sequential;

    internal static int PrefixLength => (int)PREFIX_LENGTH;

    private NuidWriter()
    {
        Refresh(out _);
    }

    public static bool TryWriteNuid(Span<char> nuidBuffer)
    {
        if(t_writer is not null)
        {
            return t_writer.TryWriteNuidCore(nuidBuffer);
        }

        return InitAndWrite(nuidBuffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InitAndWrite(Span<char> span)
    {
        t_writer = new NuidWriter();
        return t_writer.TryWriteNuidCore(span);
    }

    private bool TryWriteNuidCore(Span<char> nuidBuffer)
    {
        ulong sequential = _sequential += _increment;

        if(sequential < MAX_SEQUENTIAL)
        {
            return TryWriteNuidCore(nuidBuffer, _prefix, sequential);
        }

        return RefreshAndWrite(nuidBuffer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool RefreshAndWrite(Span<char> buffer)
        {
            char[] prefix = Refresh(out sequential);
            return TryWriteNuidCore(buffer, prefix, sequential);
        }
    }

    private static bool TryWriteNuidCore(Span<char> buffer, Span<char> prefix, ulong sequential)
    {
        if ((uint)buffer.Length < NUID_LENGTH || prefix.Length != PREFIX_LENGTH || (uint)prefix.Length > (uint)buffer.Length)
        {
            return false;
        }

        Unsafe.CopyBlockUnaligned(ref Unsafe.As<char, byte>(ref buffer[0]), ref Unsafe.As<char, byte>(ref prefix[0]), PREFIX_LENGTH * sizeof(char));

        // NOTE: We must never write to digitsPtr!
        ref char digitsPtr = ref MemoryMarshal.GetReference(Digits);

        for(nuint i = PREFIX_LENGTH; i < NUID_LENGTH; i++)
        {
            nuint digitIndex = (nuint)(sequential % BASE);
            Unsafe.Add(ref buffer[0], i) = Unsafe.Add(ref digitsPtr, digitIndex);
            sequential /= BASE;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [MemberNotNull(nameof(_prefix))]
    private char[] Refresh(out ulong sequential)
    {
        char[] prefix = _prefix = GetPrefix();
        _increment = GetIncrement();
        sequential = _sequential = GetSequential();
        return prefix;
    }

    private static uint GetIncrement()
    {
        return (uint)Random.Shared.Next(MIN_INCREMENT, MAX_INCREMENT + 1);
    }

    private static ulong GetSequential()
    {
        return (ulong)Random.Shared.NextInt64(0, (long)MAX_SEQUENTIAL + 1);
    }

    private static char[] GetPrefix(RandomNumberGenerator? rng = null)
    {
        Span<byte> randomBytes = stackalloc byte[(int)PREFIX_LENGTH];

        // TODO: For .NET 8+, use GetItems for better distribution
        if(rng == null)
        {
            RandomNumberGenerator.Fill(randomBytes);
        }
        else
        {
            rng.GetBytes(randomBytes);
        }

        char[] newPrefix = new char[PREFIX_LENGTH];

        for(int i = 0; i < randomBytes.Length; i++)
        {
            int digitIndex = (int)(randomBytes[i] % BASE);
            newPrefix[i] = Digits[digitIndex];
        }

        return newPrefix;
    }
}

