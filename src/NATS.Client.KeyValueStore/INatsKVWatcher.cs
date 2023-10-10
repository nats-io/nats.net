using System.Threading.Channels;

namespace NATS.Client.KeyValueStore;

public interface INatsKVWatcher<T> : IAsyncDisposable
{
    ChannelReader<NatsKVEntry<T?>> Msgs { get; }
}
