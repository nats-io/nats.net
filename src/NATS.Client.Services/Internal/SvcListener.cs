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
    private Task? _readLoop;
    private CancellationTokenSource? _cts;

    public SvcListener(NatsConnection nats, Channel<SvcMsg> channel, SvcMsgType type, string subject, string queueGroup, CancellationToken cancellationToken)
    {
        _nats = nats;
        _channel = channel;
        _type = type;
        _subject = subject;
        _queueGroup = queueGroup;
        _cancellationToken = cancellationToken;
    }

    public ValueTask StartAsync()
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        _readLoop = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<NatsMemoryOwner<byte>>(_subject, _queueGroup, serializer: NatsRawSerializer<NatsMemoryOwner<byte>>.Default, cancellationToken: _cts.Token))
            {
                await _channel.Writer.WriteAsync(new SvcMsg(_type, msg), _cancellationToken).ConfigureAwait(false);
            }
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_readLoop != null)
            await _readLoop;
    }
}
