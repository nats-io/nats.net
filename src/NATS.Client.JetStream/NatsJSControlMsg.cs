using System.Buffers;
using NATS.Client.Core;

namespace NATS.Client.JetStream;

internal enum NatsJSControlMsgType
{
    None,
    Heartbeat,
}

internal readonly struct NatsJSControlMsg<T>
{
    public NatsJSMsg<T?>? JSMsg { get; init; }

    public bool IsControlMsg => ControlMsgType != NatsJSControlMsgType.None;

    public NatsJSControlMsgType ControlMsgType { get; init; }
}

internal readonly struct NatsJSControlMsg
{
    public NatsJSMsg? JSMsg { get; init; }

    public bool IsControlMsg => ControlMsgType == NatsJSControlMsgType.None;

    public NatsJSControlMsgType ControlMsgType { get; init; }
}

public static class NatsJSConstants
{
    public static ReadOnlySequence<byte> Ack = new("+ACK"u8.ToArray());
}
