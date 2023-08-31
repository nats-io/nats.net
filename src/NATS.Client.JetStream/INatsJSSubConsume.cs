using System.Threading.Channels;

namespace NATS.Client.JetStream;

public interface INatsJSSubConsume<T> : IAsyncDisposable
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}

public interface INatsJSSubFetch<T> : IAsyncDisposable
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
