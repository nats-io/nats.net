using System.Threading.Channels;

namespace NATS.Client.JetStream;

/// <summary>
/// Interface to manage a <c>consume()</c> operation on a consumer.
/// </summary>
public interface INatsJSConsume : IAsyncDisposable
{
    void Stop();
}

/// <summary>
/// Interface to extract messages from a <c>consume()</c> operation on a consumer.
/// </summary>
public interface INatsJSConsume<T> : INatsJSConsume
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
