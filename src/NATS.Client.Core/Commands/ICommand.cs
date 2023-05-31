using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal interface ICommand
{
    bool IsCanceled { get; }

    void SetCancellationTimer(CancellationTimer timer);

    void Return(ObjectPool pool);

    void Write(ProtocolWriter writer);
}

#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods

internal interface IAsyncCommand : ICommand
{
    ValueTask AsValueTask();
}

internal interface IAsyncCommand<T> : ICommand
{
    ValueTask<T> AsValueTask();
}

internal interface IBatchCommand : ICommand
{
    new int Write(ProtocolWriter writer);
}
