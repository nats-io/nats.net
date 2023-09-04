using System.Threading.Channels;

namespace NATS.Client.JetStream;

public interface INatsJSSubConsume : IAsyncDisposable
{
    void Stop();
}

public interface INatsJSSubConsume<T> : INatsJSSubConsume
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}

public interface INatsJSSubFetch : IAsyncDisposable
{
    void Stop();
}

public interface INatsJSSubFetch<T> : INatsJSSubFetch
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
