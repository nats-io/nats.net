using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace NATS.Client.Services.Internal;

internal class SvcListener : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly NatsConnection _nats;
    private readonly Channel<SvcMsg> _channel;
    private readonly SvcMsgType _type;
    private readonly string _subject;
    private readonly string? _queueGroup;
    private readonly CancellationTokenSource _cts;
    private INatsSub<NatsMemoryOwner<byte>>? _sub;
    private Task? _readLoop;

    public SvcListener(ILogger logger, NatsConnection nats, Channel<SvcMsg> channel, SvcMsgType type, string subject, string? queueGroup, CancellationToken cancellationToken)
    {
        _logger = logger;
        _nats = nats;
        _channel = channel;
        _type = type;
        _subject = subject;
        _queueGroup = queueGroup;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public async ValueTask StartAsync()
    {
        _sub = await _nats.SubscribeCoreAsync(_subject, _queueGroup, serializer: NatsRawSerializer<NatsMemoryOwner<byte>>.Default, cancellationToken: _cts.Token).ConfigureAwait(false);
        _readLoop = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _sub.Msgs.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await _channel.Writer.WriteAsync(new SvcMsg(_type, msg), _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(NatsSvcLogEvents.Listener, e, "Error writing message to {Subject} listener channel", _subject);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _logger.LogError(NatsSvcLogEvents.Listener, e, "Error in {Subject} subscription", _subject);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_sub != null)
        {
            try
            {
                await _sub.DisposeAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_readLoop != null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // intentionally canceled
            }
        }
    }
}
