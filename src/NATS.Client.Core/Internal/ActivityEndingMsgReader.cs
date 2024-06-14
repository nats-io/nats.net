using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NATS.Client.Core.Internal;

// ActivityEndingMsgReader servers 2 purposes
// 1. End activity for OpenTelemetry
// 2. Keep the INatsSub<T> from being garbage collected as long as calls interacting
//    with the _inner channel are being made
// To achieve (1):
// Calls that result in a read from the _inner channel should msg.Headers?.Activity?.Dispose()
// To achieve (2):
// Synchronous calls should call GC.KeepAlive(_sub); immediately before returning
// Asynchronous calls should allocate a GCHandle.Alloc(_sub) at the start of the method,
// and then free it in a try/finally block
internal sealed class ActivityEndingMsgReader<T> : ChannelReader<NatsMsg<T>>
{
    private readonly ChannelReader<NatsMsg<T>> _inner;

    private readonly INatsSub<T> _sub;

    public ActivityEndingMsgReader(ChannelReader<NatsMsg<T>> inner, INatsSub<T> sub)
    {
        _inner = inner;
        _sub = sub;
    }

    public override bool CanCount
    {
        get
        {
            GC.KeepAlive(_sub);
            return _inner.CanCount;
        }
    }

    public override bool CanPeek
    {
        get
        {
            GC.KeepAlive(_sub);
            return _inner.CanPeek;
        }
    }

    public override int Count
    {
        get
        {
            GC.KeepAlive(_sub);
            return _inner.Count;
        }
    }

    public override Task Completion
    {
        get
        {
            GC.KeepAlive(_sub);
            return _inner.Completion;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool TryRead(out NatsMsg<T> item)
    {
        if (!_inner.TryRead(out item))
            return false;

        item.Headers?.Activity?.Dispose();

        GC.KeepAlive(_sub);
        return true;
    }

    public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        var handle = GCHandle.Alloc(_sub);
        try
        {
            return await _inner.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            handle.Free();
        }
    }

    public override async ValueTask<NatsMsg<T>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var handle = GCHandle.Alloc(_sub);
        try
        {
            var msg = await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
            msg.Headers?.Activity?.Dispose();
            return msg;
        }
        finally
        {
            handle.Free();
        }
    }

    public override bool TryPeek(out NatsMsg<T> item)
    {
        GC.KeepAlive(_sub);
        return _inner.TryPeek(out item);
    }

#if NETSTANDARD2_1 || NET6_0_OR_GREATER
    public override async IAsyncEnumerable<NatsMsg<T>> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#else
    public async IAsyncEnumerable<NatsMsg<T>> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
#endif
    {
        var handle = GCHandle.Alloc(_sub);
        try
        {
            while (await _inner.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_inner.TryRead(out var msg))
                {
                    msg.Headers?.Activity?.Dispose();
                    yield return msg;
                }
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
