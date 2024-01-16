using System.Threading.Channels;

namespace NATS.Client.Core.Internal;

internal sealed class SubWrappedChannel<T> : Channel<InFlightNatsMsg<T>, NatsMsg<T>>
{
    public SubWrappedChannel(Channel<InFlightNatsMsg<T>> channel, INatsConnection connection)
    {
        SubWrappedChannelReader<T> readChannel = new(channel, connection);
        Writer = channel.Writer;
        Reader = readChannel;
    }
}
