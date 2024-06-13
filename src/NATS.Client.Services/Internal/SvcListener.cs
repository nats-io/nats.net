using System.Threading.Channels;
using NATS.Client.Core;

namespace NATS.Client.Services.Internal;

internal class SvcListener : IAsyncDisposable
{
    private readonly NatsConnection _nats;
    private readonly Channel<SvcMsg> _channel;
    private readonly SvcMsgType _type;
    private readonly string _subject;
    private readonly string? _queueGroup;
    private readonly CancellationTokenSource _cts;
    private Task? _readLoop;

    public SvcListener(NatsConnection nats, Channel<SvcMsg> channel, SvcMsgType type, string subject, string? queueGroup, CancellationToken cancellationToken)
    {
        _nats = nats;
        _channel = channel;
        _type = type;
        _subject = subject;
        _queueGroup = queueGroup;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public async ValueTask StartAsync()
    {
        var sub = await _nats.SubscribeCoreAsync(_subject, _queueGroup, serializer: NatsRawSerializer<NatsMemoryOwner<byte>>.Default, cancellationToken: _cts.Token);
        _readLoop = Task.Run(async () =>
        {
            await using (sub)
            {
                while (await sub.Msgs.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (sub.Msgs.TryRead(out var msg))
                    {
                        await _channel.Writer.WriteAsync(new SvcMsg(_type, msg), _cts.Token).ConfigureAwait(false);
                    }
                }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_readLoop != null)
        {
            try
            {
                await _readLoop;
            }
            catch (OperationCanceledException)
            {
                // intentionally canceled
            }
        }
    }
}
