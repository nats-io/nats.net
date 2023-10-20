using NATS.Client.Core;

namespace NATS.Client.Services.Internal;

internal enum SvcMsgType
{
    Ping,
    Info,
    Stats,
}

internal readonly record struct SvcMsg(SvcMsgType MsgType, NatsMsg<NatsMemoryOwner<byte>> Msg);
