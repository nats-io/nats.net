using System.Buffers;
using System.Security.Cryptography;
using NATS.Client.Core;

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

    /// <summary>
    /// Encoding table
    /// </summary>
    private static readonly char[] SBase64Table = new[]
    {
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', '0', '1', '2', '3', '4',
        '5', '6', '7', '8', '9', Base64UrlCharacter62, Base64UrlCharacter63,
    };

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
    /// Converts a subset of an array of 8-bit unsigned integers to its equivalent string representation which is encoded with base-64-url digits. Parameters specify
    /// the subset as an offset in the input array, and the number of elements in the array to convert.
    /// </summary>
    /// <param name="inArray">An array of 8-bit unsigned integers.</param>
    /// <param name="raw">Remove padding</param>
    /// <returns>The string representation in base 64 url encoding of length elements of inArray, starting at position offset.</returns>
    /// <exception cref="ArgumentNullException">'inArray' is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">offset or length is negative OR offset plus length is greater than the length of inArray.</exception>
    public static string Encode(Span<byte> inArray, bool raw = false)
    {
        using (var owner = EncodeToMemoryOwner(inArray, raw))
        {
            var segment = owner.DangerousGetArray();
            if (segment.Array == null || segment.Array.Length == 0)
            {
                return string.Empty;
            }

            return new string(segment.Array, segment.Offset, segment.Count);
        }
    }

    public static NatsMemoryOwner<char> EncodeToMemoryOwner(Span<byte> inArray, bool raw = false)
    {
        var offset = 0;
        var length = inArray.Length;

        if (length == 0)
            return NatsMemoryOwner<char>.Empty;

        var lengthMod3 = length % 3;
        var limit = length - lengthMod3;
        var owner = NatsMemoryOwner<char>.Allocate((length + 2) / 3 * 4);
        var table = SBase64Table;
        int i, j = 0;
        var output = owner.Span;

        // takes 3 bytes from inArray and insert 4 bytes into output
        for (i = offset; i < limit; i += 3)
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

        // Where we left off before
        i = limit;

        switch (lengthMod3)
        {
        case 2:
            {
                var d0 = inArray[i];
                var d1 = inArray[i + 1];

                output[j + 0] = table[d0 >> 2];
                output[j + 1] = table[((d0 & 0x03) << 4) | (d1 >> 4)];
                output[j + 2] = table[(d1 & 0x0f) << 2];
                j += 3;
            }

            break;

        case 1:
            {
                var d0 = inArray[i];

                output[j + 0] = table[d0 >> 2];
                output[j + 1] = table[(d0 & 0x03) << 4];
                j += 2;
            }

            break;

            // default or case 0: no further operations are needed.
        }

        if (raw)
            return owner.Slice(0, j);

        for (var k = j; k < output.Length; k++)
        {
            output[k] = Base64PadCharacter;
        }

        return owner;
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
