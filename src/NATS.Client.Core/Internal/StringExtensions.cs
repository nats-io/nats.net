namespace NATS.Client.Core.Internal;

internal static class StringExtensions
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
        for (var i = 0; i < count; i++)
        {
            int c = key[i];
            span[i] = (byte)c;
        }
    }
}
