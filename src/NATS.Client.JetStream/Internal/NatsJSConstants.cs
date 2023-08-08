using System.Buffers;

namespace NATS.Client.JetStream.Internal;

internal static class NatsJSConstants
{
    public static readonly ReadOnlySequence<byte> Ack = new("+ACK"u8.ToArray());
}
