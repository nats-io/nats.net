using System.Buffers;
#if NETSTANDARD2_0 || NET9_0_OR_GREATER
using System.Buffers.Text;
#endif
using System.Security.Cryptography;

namespace NATS.Client.ObjectStore.Internal;

// Borrowed from https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/6.32.3/src/Microsoft.IdentityModel.Tokens/Base64UrlEncoder.cs
using System;
using System.Text;

/// <summary>
/// Encodes and Decodes strings as Base64Url encoding.
/// </summary>
internal static class Base64UrlEncoder
{
    private const char Base64PadCharacter = '=';
    private const char Base64Character62 = '+';
    private const char Base64Character63 = '/';
    private const char Base64UrlCharacter62 = '-';
    private const char Base64UrlCharacter63 = '_';

    public static string Sha256(ReadOnlySpan<byte> value)
    {
        Span<byte> destination = stackalloc byte[256 / 8];
        using (var sha256 = SHA256.Create())
        {
#if NETSTANDARD2_0
            var hash = sha256.ComputeHash(value.ToArray());
            hash.AsSpan().CopyTo(destination);
#else
            sha256.TryComputeHash(value, destination, out _);
#endif
        }

        return Encode(destination);
    }

    /// <summary>
    /// The following functions perform base64url encoding which differs from regular base64 encoding as follows
    /// * padding is skipped so the pad character '=' doesn't have to be percent encoded
    /// * the 62nd and 63rd regular base64 encoding characters ('+' and '/') are replace with ('-' and '_')
    /// The changes make the encoding alphabet file and URL safe.
    /// </summary>
    /// <param name="arg">string to encode.</param>
    /// <returns>Base64Url encoding of the UTF8 bytes.</returns>
    public static string Encode(string arg)
    {
        _ = arg ?? throw new ArgumentNullException(nameof(arg));

        return Encode(Encoding.UTF8.GetBytes(arg));
    }

    /// <summary>
    /// Converts the bytes to their equivalent base64url string representation.
    /// </summary>
    /// <param name="inArray">The bytes to encode.</param>
    /// <param name="raw">Remove padding.</param>
    /// <returns>The base64url encoding of the bytes.</returns>
    public static string Encode(Span<byte> inArray, bool raw = false)
    {
        var length = inArray.Length;
        if (length == 0)
            return string.Empty;

        var base64Length = (length + 2) / 3 * 4;
#if NET9_0_OR_GREATER
        // Base64Url encodes url-safe in a single pass (no separate swap). It omits padding, so for
        // the default (raw == false) the trailing '=' is appended to match base64.URLEncoding.
        char[]? rented = null;
        var chars = base64Length <= 512
            ? stackalloc char[base64Length]
            : (rented = ArrayPool<char>.Shared.Rent(base64Length)).AsSpan(0, base64Length);
        try
        {
            var written = Base64Url.EncodeToChars(inArray, chars);
            if (raw)
                return new string(chars.Slice(0, written));

            chars.Slice(written, base64Length - written).Fill(Base64PadCharacter);
            return new string(chars.Slice(0, base64Length));
        }
        finally
        {
            if (rented != null)
                ArrayPool<char>.Shared.Return(rented);
        }
#elif NETSTANDARD2_0
        // No span-based encode-to-chars on netstandard2.0: encode to ASCII bytes (read straight
        // from the input span), run the url-safe swap on the bytes, then build the string.
        var rented = ArrayPool<byte>.Shared.Rent(base64Length);
        try
        {
            _ = Base64.EncodeToUtf8(inArray, rented, out _, out var written);
            var count = MakeUrlSafe(rented.AsSpan(0, written), raw);
            return Encoding.ASCII.GetString(rented, 0, count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
#else
        // Encode straight to chars so the string is built without a separate ASCII transcode.
        char[]? rented = null;
        var chars = base64Length <= 512
            ? stackalloc char[base64Length]
            : (rented = ArrayPool<char>.Shared.Rent(base64Length)).AsSpan(0, base64Length);
        try
        {
            _ = Convert.TryToBase64Chars(inArray, chars, out var written);
            var count = MakeUrlSafe(chars.Slice(0, written), raw);
            return new string(chars.Slice(0, count));
        }
        finally
        {
            if (rented != null)
                ArrayPool<char>.Shared.Return(rented);
        }
#endif
    }

    /// <summary>
    /// Decodes the string from Base64UrlEncoded to UTF8.
    /// </summary>
    /// <param name="arg">string to decode.</param>
    /// <returns>UTF8 string.</returns>
    public static string Decode(string arg)
    {
        return Encoding.UTF8.GetString(DecodeBytes(arg));
    }

    /// <summary>
    /// Converts the specified string, base-64-url encoded to utf8 bytes.</summary>
    /// <param name="str">base64Url encoded string.</param>
    /// <returns>UTF8 bytes.</returns>
    public static byte[] DecodeBytes(string str)
    {
        _ = str ?? throw new ArgumentNullException(nameof(str));
        return UnsafeDecode(str);
    }

    // Translates standard base64 to base64url in place ('+' -> '-', '/' -> '_'). When raw is set,
    // returns the length up to the first '=' so the trailing padding is dropped; otherwise returns
    // the full length (padding kept, matching base64.URLEncoding used by other clients). net9+
    // encodes url-safe via Base64Url and needs no swap, so no helper is compiled there.
#if NETSTANDARD2_0
    private static int MakeUrlSafe(Span<byte> utf8, bool raw)
    {
        for (var i = 0; i < utf8.Length; i++)
        {
            switch (utf8[i])
            {
            case (byte)Base64Character62:
                utf8[i] = (byte)Base64UrlCharacter62;
                break;
            case (byte)Base64Character63:
                utf8[i] = (byte)Base64UrlCharacter63;
                break;
            case (byte)Base64PadCharacter:
                if (raw)
                    return i;
                break;
            }
        }

        return utf8.Length;
    }
#elif NETSTANDARD2_1
    private static int MakeUrlSafe(Span<char> chars, bool raw)
    {
        for (var i = 0; i < chars.Length; i++)
        {
            switch (chars[i])
            {
            case Base64Character62:
                chars[i] = Base64UrlCharacter62;
                break;
            case Base64Character63:
                chars[i] = Base64UrlCharacter63;
                break;
            case Base64PadCharacter:
                if (raw)
                    return i;
                break;
            }
        }

        return chars.Length;
    }
#elif !NET9_0_OR_GREATER
    private static int MakeUrlSafe(Span<char> chars, bool raw)
    {
        chars.Replace(Base64Character62, Base64UrlCharacter62);
        chars.Replace(Base64Character63, Base64UrlCharacter63);

        if (raw)
            return chars.LastIndexOfAnyExcept(Base64PadCharacter) + 1;

        return chars.Length;
    }
#endif

    private static unsafe byte[] UnsafeDecode(string str)
    {
        var mod = str.Length % 4;
        if (mod == 1)
            throw new FormatException(nameof(str));

        var needReplace = false;
        var decodedLength = str.Length + ((4 - mod) % 4);

        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] == Base64UrlCharacter62 || str[i] == Base64UrlCharacter63)
            {
                needReplace = true;
                break;
            }
        }

        if (needReplace)
        {
            string decodedString = new(char.MinValue, decodedLength);
            fixed (char* dest = decodedString)
            {
                var i = 0;
                for (; i < str.Length; i++)
                {
                    if (str[i] == Base64UrlCharacter62)
                        dest[i] = Base64Character62;
                    else if (str[i] == Base64UrlCharacter63)
                        dest[i] = Base64Character63;
                    else
                        dest[i] = str[i];
                }

                for (; i < decodedLength; i++)
                    dest[i] = Base64PadCharacter;
            }

            return Convert.FromBase64String(decodedString);
        }
        else
        {
            if (decodedLength == str.Length)
            {
                return Convert.FromBase64String(str);
            }
            else
            {
                string decodedString = new(char.MinValue, decodedLength);
                fixed (char* src = str)
                {
                    fixed (char* dest = decodedString)
                    {
                        Buffer.MemoryCopy(src, dest, str.Length * 2, str.Length * 2);
                        dest[str.Length] = Base64PadCharacter;
                        if (str.Length + 2 == decodedLength)
                            dest[str.Length + 1] = Base64PadCharacter;
                    }
                }

                return Convert.FromBase64String(decodedString);
            }
        }
    }
}
