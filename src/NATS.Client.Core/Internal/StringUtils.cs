namespace NATS.Client.Core.Internal;

internal static class StringUtils
{
    /// <summary>
    /// Allocation free ASCII buffer writer.
    /// There is no protection if string isn't ASCII.
    /// </summary>
    /// <param name="key">ASCII string</param>
    /// <param name="span">Target memory location. Assumed to be large enough.</param>
    public static void WriteASCIIBytes(this string key, Span<byte> span)
    {
        var count = Math.Min(key.Length, span.Length);
        for (int i = 0; i < count; i++)
        {
            int c = key[i];
            span[i] = (byte)c;
        }
    }
}
