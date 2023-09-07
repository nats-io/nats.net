using System.Threading.Channels;

namespace NATS.Client.JetStream;

public interface INatsJSFetch : IAsyncDisposable
{
    void Stop();
}

public interface INatsJSFetch<T> : INatsJSFetch
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
