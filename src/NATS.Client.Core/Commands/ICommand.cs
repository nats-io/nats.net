using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal interface ICommand
{
    void Return(ObjectPool pool);

    void Write(ProtocolWriter writer);
}

internal interface IBatchCommand : ICommand
{
    new int Write(ProtocolWriter writer);
}
