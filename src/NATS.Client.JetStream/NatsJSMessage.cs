using NATS.Client.Core;

namespace NATS.Client.JetStream;

internal enum NatsJSControlMsgType
{
    None,
    Heartbeat,
}

/// <summary>
/// NATS JetStream message with <see cref="NatsMsg{T}"/> and control messages.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
internal readonly struct NatsJSMessage<T>
{
    public NatsMsg<T>? Msg { get; init; }

    public bool IsControlMsg => ControlMsgType == NatsJSControlMsgType.None;

    public NatsJSControlMsgType ControlMsgType { get; init; }
}
