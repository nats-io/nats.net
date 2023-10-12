using System.Threading.Channels;
using NATS.Client.Core;

namespace NATS.Client.JetStream;

/// <summary>
/// Interface to manage a <c>fetch()</c> operation on a consumer.
/// </summary>
public interface INatsJSFetch : IAsyncDisposable
{
    /// <summary>
    /// The reason why the fetch has ended
    /// </summary>
    NatsSubEndReason EndReason { get; }

    void Stop();
}

/// <summary>
/// Interface to extract messages from a <c>fetch()</c> operation on a consumer.
/// </summary>
public interface INatsJSFetch<T> : INatsJSFetch
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
