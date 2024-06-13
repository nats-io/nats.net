using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
#if NETSTANDARD2_0 || NETSTANDARD2_1
using Random = NATS.Client.Core.Internal.NetStandardExtensions.Random;
#endif

namespace NATS.Client.Core.Internal;

#if NET6_0_OR_GREATER
[SkipLocalsInit]
#endif
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

#if NET6_0_OR_GREATER
    private char[] _prefix;
#else
    private char[] _prefix = null!;
#endif
    private ulong _increment;
    private ulong _sequential;

    private NuidWriter()
    {
        Refresh(out _);
    }

    private static ReadOnlySpan<char> Digits => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
#if NETSTANDARD2_0
        .AsSpan()
#endif
    ;

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
#if NETSTANDARD2_0
            return new string(buffer.ToArray());
#else
            return new string(buffer);
#endif
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

        // write backwards so the last two characters change the fastest
        for (var i = NuidLength; i > PrefixLength;)
        {
            i--;
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
#if NETSTANDARD2_0
        var randomBytes = new byte[(int)PrefixLength];

        if (rng == null)
        {
            using var randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(randomBytes);
        }
#else
        Span<byte> randomBytes = stackalloc byte[(int)PrefixLength];

        // TODO: For .NET 8+, use GetItems for better distribution
        if (rng == null)
        {
            RandomNumberGenerator.Fill(randomBytes);
        }
#endif
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
#if NET6_0_OR_GREATER
    [MemberNotNull(nameof(_prefix))]
#endif
    private char[] Refresh(out ulong sequential)
    {
        var prefix = _prefix = GetPrefix();
        _increment = GetIncrement();
        sequential = _sequential = GetSequential();
        return prefix;
    }
}
