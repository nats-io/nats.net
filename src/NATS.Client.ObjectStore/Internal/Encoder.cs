using System.Buffers;
#if NET9_0_OR_GREATER
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

#if !NET9_0_OR_GREATER
    // base64url alphabet (the 63rd/64th entries are '-'/'_'), so the table encodes url-safe directly.
    private static readonly char[] SBase64Table =
    {
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', Base64UrlCharacter62, Base64UrlCharacter63,
    };
#endif

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
#else
        // Pre-net9 there is no url-safe base64 encoder, so use the lookup table (which maps directly
        // to the url-safe alphabet). Encode into a stack/pooled buffer with an unsafe pointer loop
        // (no bounds checks), so only the result string is allocated.
        char[]? rented = null;
        var chars = base64Length <= 512
            ? stackalloc char[base64Length]
            : (rented = ArrayPool<char>.Shared.Rent(base64Length)).AsSpan(0, base64Length);
        try
        {
            var lengthMod3 = length % 3;
            var limit = length - lengthMod3;
            var j = 0;

            unsafe
            {
                // Pin the table and output and index them without bounds checks; inArray stays a
                // checked span (its accesses are bounded by limit, derived from its own length).
                fixed (char* dst = chars, tbl = SBase64Table)
                {
                    // Each 3 input bytes map to 4 output chars.
                    for (var i = 0; i < limit; i += 3)
                    {
                        int d0 = inArray[i];
                        int d1 = inArray[i + 1];
                        int d2 = inArray[i + 2];

                        dst[j + 0] = tbl[d0 >> 2];
                        dst[j + 1] = tbl[((d0 & 0x03) << 4) | (d1 >> 4)];
                        dst[j + 2] = tbl[((d1 & 0x0f) << 2) | (d2 >> 6)];
                        dst[j + 3] = tbl[d2 & 0x3f];
                        j += 4;
                    }

                    switch (lengthMod3)
                    {
                    case 2:
                        {
                            int d0 = inArray[limit];
                            int d1 = inArray[limit + 1];
                            dst[j + 0] = tbl[d0 >> 2];
                            dst[j + 1] = tbl[((d0 & 0x03) << 4) | (d1 >> 4)];
                            dst[j + 2] = tbl[(d1 & 0x0f) << 2];
                            j += 3;
                        }

                        break;

                    case 1:
                        {
                            int d0 = inArray[limit];
                            dst[j + 0] = tbl[d0 >> 2];
                            dst[j + 1] = tbl[(d0 & 0x03) << 4];
                            j += 2;
                        }

                        break;
                    }

                    if (!raw)
                    {
                        for (var k = j; k < base64Length; k++)
                            dst[k] = Base64PadCharacter;
                        j = base64Length;
                    }

                    return new string(dst, 0, j);
                }
            }
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
