using System.Buffers;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace NATS.Client.ObjectStore.Internal;

internal enum NatsOBSubCommand
{
    Msg,
    Ready,
}

internal readonly struct NatsOBSubMsg<T>
{
    public NatsOBSubMsg()
    {
    }

    public NatsOBSubCommand Command { get; init; } = default;

    public NatsJSMsg<T> Msg { get; init; } = default;
}

internal class NatsOBSub<T> : NatsSubBase
{
    private readonly NatsJSContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly NatsConnection _nats;
    private readonly NatsHeaderParser _headerParser;
    private readonly INatsSerializer _serializer;
    private readonly ChannelWriter<NatsOBSubMsg<T?>> _commands;

    public NatsOBSub(
        NatsJSContext context,
        Channel<NatsOBSubMsg<T?>> commandChannel,
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
        _serializer = opts?.Serializer ?? context.Connection.Opts.Serializer;
        _nats = context.Connection;
        _headerParser = _nats.HeaderParser;
        _commands = commandChannel.Writer;
        _nats.ConnectionOpened += OnConnectionOpened;
    }

    public override async ValueTask ReadyAsync()
    {
        await base.ReadyAsync();
        await _commands.WriteAsync(new NatsOBSubMsg<T?> { Command = NatsOBSubCommand.Ready }, _cancellationToken).ConfigureAwait(false);
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
        var msg = new NatsJSMsg<T?>(NatsMsg<T?>.Build(subject, replyTo, headersBuffer, payloadBuffer, _nats, _headerParser, _serializer), _context);
        await _commands.WriteAsync(new NatsOBSubMsg<T?> { Command = NatsOBSubCommand.Msg, Msg = msg }, _cancellationToken).ConfigureAwait(false);
    }

    protected override void TryComplete()
    {
    }

    private void OnConnectionOpened(object? sender, string e)
    {
        _commands.TryWrite(new NatsOBSubMsg<T?> { Command = NatsOBSubCommand.Ready });
    }
}
