using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NATS.Client.Core.Internal;

internal sealed class ActivityEndingMsgReader<T> : ChannelReader<NatsMsg<T>>
{
    private readonly ChannelReader<NatsMsg<T>> _inner;

    public ActivityEndingMsgReader(ChannelReader<NatsMsg<T>> inner) => _inner = inner;

    public override bool CanCount => _inner.CanCount;

    public override bool CanPeek => _inner.CanPeek;

    public override int Count => _inner.Count;

    public override Task Completion => _inner.Completion;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool TryRead(out NatsMsg<T> item)
    {
        if (!_inner.TryRead(out item))
            return false;

        item.Activity?.Dispose();
        return true;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) => _inner.WaitToReadAsync(cancellationToken);

    public override ValueTask<NatsMsg<T>> ReadAsync(CancellationToken cancellationToken = default) => _inner.ReadAsync(cancellationToken);

    public override bool TryPeek(out NatsMsg<T> item) => _inner.TryPeek(out item);

    public override IAsyncEnumerable<NatsMsg<T>> ReadAllAsync(CancellationToken cancellationToken = default) => _inner.ReadAllAsync(cancellationToken);
}
