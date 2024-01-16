using System.Threading.Channels;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

public sealed class SubWrappedChannelReader<T> : ChannelReader<NatsMsg<T>>
{
    private readonly ChannelReader<InFlightNatsMsg<T>> _channel;
    private readonly INatsConnection? _connection;
    internal PooledValueTaskSource<T>? _internalPooledSource;


    internal SubWrappedChannelReader(ChannelReader<InFlightNatsMsg<T>> channel, INatsConnection? connection)
    {
        _channel = channel;
        _connection = connection;
    }

    public override bool CanPeek => base.CanPeek;

    public override Task Completion => _channel.Completion;

    public override bool CanCount => _channel.CanCount;

    public override int Count => _channel.Count;

    public override ValueTask<NatsMsg<T>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var read = _channel.ReadAsync(cancellationToken);
        if (read.IsCompletedSuccessfully)
        {
            return new ValueTask<NatsMsg<T>>(read.GetAwaiter().GetResult().ToNatsMsg(_connection));
        }
        else
        {
            return doReadSlow(read);
        }
    }

    private ValueTask<NatsMsg<T>> doReadSlow(ValueTask<InFlightNatsMsg<T>> read)
    {
        PooledValueTaskSource<T>? pvts = null;
        if ((pvts = Interlocked.Exchange(ref _internalPooledSource, null)) == null)
        {
            pvts = PooledValueTaskSource<T>.RentOrGet(this);
        }

        return pvts.ToValueTaskAsync(read, _connection);
    }

    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) => _channel.WaitToReadAsync(cancellationToken);

    public override bool TryRead(out NatsMsg<T> item)
    {
        if (_channel.TryRead(out var inFlight))
        {
            item = inFlight.ToNatsMsg(_connection);
            return true;
        }

        item = default;
        return false;
    }

    public override bool TryPeek(out NatsMsg<T> item)
    {
        if (_channel.TryPeek(out var inFlight))
        {
            item = inFlight.ToNatsMsg(_connection);
            return true;
        }

        item = default;
        return false;
    }
}
