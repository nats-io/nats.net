using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NATS.Client.Core.Internal;

[SkipLocalsInit]
internal sealed class NuidWriter
{
    internal const nuint NuidLength = PrefixLength + SequentialLength;
    private const nuint Base = 62;
    private const ulong MaxSequential = 839299365868340224; // 62^10
    private const uint PrefixLength = 12;
    private const nuint SequentialLength = 10;
    private const int MinIncrement = 33;
    private const int MaxIncrement = 333;

    [ThreadStatic]
    private static NuidWriter? _writer;

    private char[] _prefix;
    private ulong _increment;
    private ulong _sequential;

    private NuidWriter()
    {
        Refresh(out _);
    }

    private static ReadOnlySpan<char> Digits => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static bool TryWriteNuid(Span<char> nuidBuffer)
    {
        if (_writer is not null)
        {
            return _writer.TryWriteNuidCore(nuidBuffer);
        }

        return InitAndWrite(nuidBuffer);
    }

    public static string NewNuid()
    {
        Span<char> buffer = stackalloc char[22];
        if (TryWriteNuid(buffer))
        {
            return new string(buffer);
        }

        throw new InvalidOperationException("Internal error: can't generate nuid");
    }

    private static bool TryWriteNuidCore(Span<char> buffer, Span<char> prefix, ulong sequential)
    {
        if ((uint)buffer.Length < NuidLength || prefix.Length != PrefixLength)
        {
            return false;
        }

        Unsafe.CopyBlockUnaligned(ref Unsafe.As<char, byte>(ref buffer[0]), ref Unsafe.As<char, byte>(ref prefix[0]), PrefixLength * sizeof(char));

        // NOTE: We must never write to digitsPtr!
        ref var digitsPtr = ref MemoryMarshal.GetReference(Digits);

        for (nuint i = PrefixLength; i < NuidLength; i++)
        {
            var digitIndex = (nuint)(sequential % Base);
            Unsafe.Add(ref buffer[0], i) = Unsafe.Add(ref digitsPtr, digitIndex);
            sequential /= Base;
        }

        return true;
    }

    private static uint GetIncrement()
    {
        return (uint)Random.Shared.Next(MinIncrement, MaxIncrement + 1);
    }

    private static ulong GetSequential()
    {
        return (ulong)Random.Shared.NextInt64(0, (long)MaxSequential + 1);
    }

    private static char[] GetPrefix(RandomNumberGenerator? rng = null)
    {
        Span<byte> randomBytes = stackalloc byte[(int)PrefixLength];

        // TODO: For .NET 8+, use GetItems for better distribution
        if (rng == null)
        {
            RandomNumberGenerator.Fill(randomBytes);
        }
        else
        {
            rng.GetBytes(randomBytes);
        }

        var newPrefix = new char[PrefixLength];

        for (var i = 0; i < randomBytes.Length; i++)
        {
            var digitIndex = (int)(randomBytes[i] % Base);
            newPrefix[i] = Digits[digitIndex];
        }

        return newPrefix;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InitAndWrite(Span<char> span)
    {
        _writer = new NuidWriter();
        return _writer.TryWriteNuidCore(span);
    }

    private bool TryWriteNuidCore(Span<char> nuidBuffer)
    {
        var sequential = _sequential += _increment;

        if (sequential < MaxSequential)
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    [MemberNotNull(nameof(_prefix))]
    private char[] Refresh(out ulong sequential)
    {
        var prefix = _prefix = GetPrefix();
        _increment = GetIncrement();
        sequential = _sequential = GetSequential();
        return prefix;
    }
}
