using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public readonly struct NatsJSPublishConcurrentFuture : IAsyncDisposable
{
    private readonly NatsSub<PubAckResponse> _sub;

    public NatsJSPublishConcurrentFuture(NatsSub<PubAckResponse> sub) => _sub = sub;

    public async ValueTask<PubAckResponse> GetResponseAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var msg in _sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (msg.Data == null)
            {
                throw new NatsJSException("No response data received");
            }

            return msg.Data;
        }

        throw new NatsJSPublishNoResponseException();
    }

    public async ValueTask DisposeAsync() => await _sub.DisposeAsync();
}
