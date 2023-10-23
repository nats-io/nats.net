using System.Threading.Channels;
using NATS.Client.Core;

namespace NATS.Client.Services.Internal;

internal class SvcListener : IAsyncDisposable
{
    private readonly NatsConnection _nats;
    private readonly Channel<SvcMsg> _channel;
    private readonly SvcMsgType _type;
    private readonly string _subject;
    private readonly string _queueGroup;
    private readonly CancellationToken _cancellationToken;
    private INatsSub<NatsMemoryOwner<byte>>? _sub;
    private Task? _readLoop;

    public SvcListener(NatsConnection nats, Channel<SvcMsg> channel, SvcMsgType type, string subject, string queueGroup, CancellationToken cancellationToken)
    {
        _nats = nats;
        _channel = channel;
        _type = type;
        _subject = subject;
        _queueGroup = queueGroup;
        _cancellationToken = cancellationToken;
    }

    public async ValueTask StartAsync()
    {
        _sub = await _nats.SubscribeAsync<NatsMemoryOwner<byte>>(_subject, queueGroup: _queueGroup, cancellationToken: _cancellationToken);
        _readLoop = Task.Run(async () =>
        {
            while (await _sub.Msgs.WaitToReadAsync(_cancellationToken).ConfigureAwait(false))
            {
                while (_sub.Msgs.TryRead(out var msg))
                {
                    await _channel.Writer.WriteAsync(new SvcMsg(_type, msg), _cancellationToken).ConfigureAwait(false);
                }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_sub != null)
            await _sub.DisposeAsync();
        if (_readLoop != null)
            await _readLoop;
    }
}
