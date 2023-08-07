namespace NATS.Client.Core.Commands;

internal static class CommandConstants
{
    // All constants uses C# compiler's optimization for static byte[] data

    // string.Join(",", Encoding.ASCII.GetBytes("r\n"))
    public static ReadOnlySpan<byte> NewLine => new byte[] { 13, 10 };

    // string.Join(",", Encoding.ASCII.GetBytes("CONNECT "))
    public static ReadOnlySpan<byte> ConnectWithPadding => new byte[] { 67, 79, 78, 78, 69, 67, 84, 32 };

    // string.Join(",", Encoding.ASCII.GetBytes("PUB "))
    public static ReadOnlySpan<byte> PubWithPadding => new byte[] { 80, 85, 66, 32 };

    // string.Join(",", Encoding.ASCII.GetBytes("HPUB "))
    public static ReadOnlySpan<byte> HPubWithPadding => new byte[] { 72, 80, 85, 66, 32 };

    // string.Join(",", Encoding.ASCII.GetBytes("SUB "))
    public static ReadOnlySpan<byte> SubWithPadding => new byte[] { 83, 85, 66, 32 };

    // string.Join(",", Encoding.ASCII.GetBytes("UNSUB "))
    public static ReadOnlySpan<byte> UnsubWithPadding => new byte[] { 85, 78, 83, 85, 66, 32 };

    // string.Join(",", Encoding.ASCII.GetBytes("PING\r\n"))
    public static ReadOnlySpan<byte> PingNewLine => new byte[] { 80, 73, 78, 71, 13, 10 };

    // string.Join(",", Encoding.ASCII.GetBytes("PONG\r\n"))
    public static ReadOnlySpan<byte> PongNewLine => new byte[] { 80, 79, 78, 71, 13, 10 };

    // string.Join(",", Encoding.ASCII.GetBytes("NATS/1.0"))
    public static ReadOnlySpan<byte> NatsHeaders10 => new byte[] { 78, 65, 84, 83, 47, 49, 46, 48 };

    // string.Join(",", Encoding.ASCII.GetBytes("NATS/1.0\r\n"))
    public static ReadOnlySpan<byte> NatsHeaders10NewLine => new byte[] { 78, 65, 84, 83, 47, 49, 46, 48, 13, 10 };
}
