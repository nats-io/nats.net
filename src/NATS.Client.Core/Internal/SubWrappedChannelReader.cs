using System.Threading.Channels;

namespace NATS.Client.Core.Internal;

public sealed class SubWrappedChannelReader<T> : ChannelReader<NatsMsg<T>>
{
    private readonly ChannelReader<InFlightNatsMsg<T>> _channel;
    private readonly INatsConnection _connection;

    internal SubWrappedChannelReader(ChannelReader<InFlightNatsMsg<T>> channel, INatsConnection connection)
    {
        _channel = channel;
        _connection = connection;
    }

    public override bool CanPeek => base.CanPeek;

    public override Task Completion => _channel.Completion;

    public override bool CanCount => _channel.CanCount;

    public override int Count => _channel.Count;

    public override async ValueTask<NatsMsg<T>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var inFlight = await _channel.ReadAsync(cancellationToken).ConfigureAwait(true);
        return inFlight.ToNatsMsg(_connection);
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
