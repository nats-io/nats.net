using System.Threading.Channels;

namespace NATS.Client.KeyValueStore;

/// <summary>
/// Key Value Store Watcher
/// </summary>
/// <typeparam name="T">Serialized data type</typeparam>
public interface INatsKVWatcher<T> : IAsyncDisposable
{
    /// <summary>
    /// Entries channel reader
    /// </summary>
    ChannelReader<NatsKVEntry<T?>> Entries { get; }
}
