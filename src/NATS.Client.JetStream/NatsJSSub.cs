using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace NATS.Client.JetStream;

/// <summary>
/// NATS JetStream Subscription with JetStream control message support.
/// </summary>
/// <typeparam name="T">User message type</typeparam>
internal class NatsJSSub<T> : NatsSubBase
{
    private readonly Channel<NatsJSControlMsg<T?>> _msgs;

    internal NatsJSSub(
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts,
        INatsSerializer serializer)
        : base(connection, manager, subject, opts)
    {
        var channelOptions = NatsSub.GetChannelOptions(opts?.ChannelOptions);
        _msgs = Channel.CreateBounded<NatsJSControlMsg<T?>>(
            new BoundedChannelOptions(channelOptions.Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        Serializer = serializer;
    }

    public ChannelReader<NatsJSControlMsg<T?>> Msgs => _msgs.Reader;

    public ChannelWriter<NatsJSControlMsg<T?>> MsgWriter => _msgs.Writer;

    private INatsSerializer Serializer { get; }

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        if (subject == Subject)
        {
            // TODO: introspect JS control messages
            var msg = NatsMsg<T?>.Build(
                subject,
                replyTo,
                headersBuffer,
                payloadBuffer,
                Connection,
                Connection.HeaderParser,
                Serializer);
            await _msgs.Writer.WriteAsync(new NatsJSControlMsg<T?>
            {
                JSMsg = new NatsJSMsg<T?> { Msg = msg },
                ControlMsgType = NatsJSControlMsgType.Heartbeat,
            }).ConfigureAwait(false);
        }
        else
        {
            try
            {
                var msg = NatsMsg<T?>.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser,
                    Serializer);

                await _msgs.Writer.WriteAsync(new NatsJSControlMsg<T?>
                {
                    JSMsg = new NatsJSMsg<T?> { Msg = msg },
                    ControlMsgType = NatsJSControlMsgType.None,
                }).ConfigureAwait(false);

                DecrementMaxMsgs();
            }
            catch (Exception e)
            {
                var payload = new Memory<byte>(new byte[payloadBuffer.Length]);
                payloadBuffer.CopyTo(payload.Span);

                Memory<byte> headers = default;
                if (headersBuffer != null)
                {
                    headers = new Memory<byte>(new byte[headersBuffer.Value.Length]);
                }

                SetException(new NatsSubException($"Message error: {e.Message}", e, payload, headers));
            }
        }
    }

    protected override void TryComplete() => _msgs.Writer.TryComplete();
}

internal class NatsJSSubModelBuilder<T> : INatsSubBuilder<NatsJSSub<T>>
{
    private static readonly ConcurrentDictionary<INatsSerializer, NatsJSSubModelBuilder<T>> Cache = new();
    private readonly INatsSerializer _serializer;

    public NatsJSSubModelBuilder(INatsSerializer serializer) => _serializer = serializer;

    public static NatsJSSubModelBuilder<T> For(INatsSerializer serializer) =>
        Cache.GetOrAdd(serializer, static s => new NatsJSSubModelBuilder<T>(s));

    public NatsJSSub<T> Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager)
    {
        return new NatsJSSub<T>(connection, manager, subject, opts, _serializer);
    }
}

/// <summary>
/// NATS JetStream Subscription with JetStream control message support.
/// </summary>
internal class NatsJSSub : NatsSubBase
{
    private readonly Channel<NatsJSControlMsg> _msgs;

    internal NatsJSSub(
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts)
        : base(connection, manager, subject, opts) =>
        _msgs = Channel.CreateBounded<NatsJSControlMsg>(
            NatsSub.GetChannelOptions(opts?.ChannelOptions));

    public ChannelReader<NatsJSControlMsg> Msgs => _msgs.Reader;

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        if (subject == Subject)
        {
            // TODO: introspect JS control messages
            await _msgs.Writer.WriteAsync(new NatsJSControlMsg
            {
                JSMsg = default,
                ControlMsgType = NatsJSControlMsgType.Heartbeat,
            }).ConfigureAwait(false);
        }
        else
        {
            try
            {
                var msg = NatsMsg.Build(
                    subject,
                    replyTo,
                    headersBuffer,
                    payloadBuffer,
                    Connection,
                    Connection.HeaderParser);

                await _msgs.Writer.WriteAsync(new NatsJSControlMsg
                {
                    JSMsg = new NatsJSMsg { Msg = msg },
                    ControlMsgType = NatsJSControlMsgType.None,
                }).ConfigureAwait(false);

                DecrementMaxMsgs();
            }
            catch (Exception e)
            {
                var payload = new Memory<byte>(new byte[payloadBuffer.Length]);
                payloadBuffer.CopyTo(payload.Span);

                Memory<byte> headers = default;
                if (headersBuffer != null)
                {
                    headers = new Memory<byte>(new byte[headersBuffer.Value.Length]);
                }

                SetException(new NatsSubException($"Message error: {e.Message}", e, payload, headers));
            }
        }
    }

    protected override void TryComplete() => _msgs.Writer.TryComplete();
}

internal class NatsJSSubBuilder : INatsSubBuilder<NatsJSSub>
{
    public static readonly NatsJSSubBuilder Default = new();

    public NatsJSSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager)
    {
        return new NatsJSSub(connection, manager, subject, opts);
    }
}
