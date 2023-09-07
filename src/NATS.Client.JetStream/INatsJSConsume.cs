using System.Threading.Channels;

namespace NATS.Client.JetStream;

public interface INatsJSConsume : IAsyncDisposable
{
    void Stop();
}

public interface INatsJSConsume<T> : INatsJSConsume
{
    ChannelReader<NatsJSMsg<T?>> Msgs { get; }
}
