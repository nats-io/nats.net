using System.Buffers;
using System.Text;

namespace NATS.Client.JetStream.Internal;

internal static class NatsJSConstants
{
    public static readonly ReadOnlySequence<byte> Ack = new(Encoding.ASCII.GetBytes("+ACK"));
}
