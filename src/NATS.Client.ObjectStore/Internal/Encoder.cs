using System.Buffers;
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

        // Base64.EncodeToUtf8 reads the input span directly (no array copy) and the output is
        // ASCII, so the url-safe swap runs on the bytes and the string is built from them.
        var base64Length = (length + 2) / 3 * 4;
#if NETSTANDARD2_0
        var rented = ArrayPool<byte>.Shared.Rent(base64Length);
        try
        {
            _ = System.Buffers.Text.Base64.EncodeToUtf8(inArray, rented, out _, out var written);
            var count = MakeUrlSafe(rented.AsSpan(0, written), raw);
            return Encoding.ASCII.GetString(rented, 0, count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
#else
        byte[]? rented = null;
        var utf8 = base64Length <= 512
            ? stackalloc byte[base64Length]
            : (rented = ArrayPool<byte>.Shared.Rent(base64Length)).AsSpan(0, base64Length);
        try
        {
            _ = System.Buffers.Text.Base64.EncodeToUtf8(inArray, utf8, out _, out var written);
            var count = MakeUrlSafe(utf8.Slice(0, written), raw);
            return Encoding.ASCII.GetString(utf8.Slice(0, count));
        }
        finally
        {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
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

    // Translates standard base64 to base64url in place ('+' -> '-', '/' -> '_'). When raw is
    // set, returns the length up to the first '=' so the trailing padding is dropped; otherwise
    // returns the full length (padding kept, matching base64.URLEncoding used by other clients).
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
        utf8.Replace((byte)Base64Character62, (byte)Base64UrlCharacter62);
        utf8.Replace((byte)Base64Character63, (byte)Base64UrlCharacter63);

        if (raw && utf8.EndsWith((byte)Base64PadCharacter))
        {
            return utf8.LastIndexOfAnyExcept((byte)Base64PadCharacter) + 1;
        }

        return utf8.Length;
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
