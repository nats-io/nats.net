using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NATS.Client.JetStream.Internal;

internal sealed class ActivityEndingJSMsgReader<T> : ChannelReader<NatsJSMsg<T>>
{
    private readonly ChannelReader<NatsJSMsg<T>> _inner;

    public ActivityEndingJSMsgReader(ChannelReader<NatsJSMsg<T>> inner) => _inner = inner;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool TryRead(out NatsJSMsg<T> item)
    {
        if (!_inner.TryRead(out item))
            return false;

        item.Activity?.Dispose();
        return true;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) => _inner.WaitToReadAsync(cancellationToken);
}
