using System.Threading.Channels;

namespace NATS.Client.JetStream;

/// <summary>
/// Interface to extract messages from a <c>fetch()</c> operation on a consumer.
/// </summary>
public interface INatsJSFetch<T> : IAsyncDisposable
{
    /// <summary>
    /// User messages received from the consumer.
    /// </summary>
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
