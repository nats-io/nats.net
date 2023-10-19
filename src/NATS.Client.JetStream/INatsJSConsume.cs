using System.Threading.Channels;

namespace NATS.Client.JetStream;

/// <summary>
/// Interface to extract messages from a <c>consume()</c> operation on a consumer.
/// </summary>
public interface INatsJSConsume<T> : IAsyncDisposable
{
    /// <summary>
    /// Messages received from the consumer.
    /// </summary>
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }

    /// <summary>
    /// Stop the consumer gracefully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This will wait for any inflight messages to be processed before stopping.
    /// </para>
    /// <para>
    /// Disposing would stop consuming immediately. This might leave messages behind
    /// without acknowledgement. Which is fine, messages will be scheduled for redelivery,
    /// however, it might not be the desired behavior.
    /// </para>
    /// </remarks>
    void Stop();
}
