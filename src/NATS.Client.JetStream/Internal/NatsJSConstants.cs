using System.Buffers;
using System.Text;

namespace NATS.Client.JetStream.Internal;

public static class NatsJSConstants
{
    public static readonly ReadOnlySequence<byte> Ack = new(Encoding.ASCII.GetBytes("+ACK"));
    public static readonly ReadOnlySequence<byte> Nak = new(Encoding.ASCII.GetBytes("-NAK"));
    public static readonly ReadOnlySequence<byte> AckProgress = new(Encoding.ASCII.GetBytes("+WPI"));
    public static readonly ReadOnlySequence<byte> AckTerminate = new(Encoding.ASCII.GetBytes("+TERM"));
}
