using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace NATS.Client.KeyValueStore.Internal;

internal class NatsKVWatchSub<T> : NatsSubBase
{
    private readonly NatsJSContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly NatsConnection _nats;
    private readonly INatsDeserialize<T> _serializer;
    private readonly ChannelWriter<NatsKVWatchCommandMsg<T>> _commands;

    public NatsKVWatchSub(
        NatsJSContext context,
        Channel<NatsKVWatchCommandMsg<T>> commandChannel,
        INatsDeserialize<T> serializer,
        NatsSubOpts? opts,
        CancellationToken cancellationToken)
        : base(
            connection: context.Connection,
            manager: context.Connection.SubscriptionManager,
            subject: context.NewInbox(),
            queueGroup: default,
            opts)
    {
        _context = context;
        _cancellationToken = cancellationToken;
        _serializer = serializer;
        _nats = context.Connection;
        _commands = commandChannel.Writer;
        _nats.ConnectionOpened += OnConnectionOpened;
    }

    public override async ValueTask ReadyAsync()
    {
        await base.ReadyAsync();
        await _commands.WriteAsync(new NatsKVWatchCommandMsg<T> { Command = NatsKVWatchCommand.Ready }, _cancellationToken).ConfigureAwait(false);
    }

    public override ValueTask DisposeAsync()
    {
        _nats.ConnectionOpened -= OnConnectionOpened;
        return base.DisposeAsync();
    }

    protected override async ValueTask ReceiveInternalAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer)
    {
        var msg = new NatsJSMsg<T>(
            ParseMsg(
                activitySource: Telemetry.NatsInternalActivities,
                activityName: "kv_cmd_receive",
                subject: subject,
                replyTo: replyTo,
                headersBuffer,
                in payloadBuffer,
                Connection,
                Connection.HeaderParser,
                serializer: _serializer),
            _context);

        await _commands.WriteAsync(new NatsKVWatchCommandMsg<T> { Command = NatsKVWatchCommand.Msg, Msg = msg }, _cancellationToken).ConfigureAwait(false);
    }

    protected override void TryComplete()
    {
    }

    private ValueTask OnConnectionOpened(object? sender, NatsEventArgs args)
    {
        // result is discarded, so this code is assumed to not be failing
        _ = _commands.TryWrite(new NatsKVWatchCommandMsg<T> { Command = NatsKVWatchCommand.Ready });
        return default;
    }
}
