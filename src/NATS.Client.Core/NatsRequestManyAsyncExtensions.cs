using System.Buffers;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsRequestManyAsyncExtensions
{
    // RequestManyAsync methods
    // Same as PublishAsync with the following changes
    // - Response is IAsyncEnumerable<NatsMsg>
    // - PubOpts is called requestOpts
    // - add SubOpts replyOpts
    //   - if replyOpts.Timeout == null then set to NatsOptions.RequestTimeout
    public static async IAsyncEnumerable<NatsMsg> RequestManyAsync(
        this NatsConnection nats,
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inboxSubscriber = nats.InboxSubscriber;
        await inboxSubscriber.EnsureStartedAsync().ConfigureAwait(false);

        var cancellationTimer = nats.GetCancellationTimer(cancellationToken);

        if (!nats.ObjectPool.TryRent<MsgWrapper>(out var wrapper))
        {
            wrapper = new MsgWrapper();
        }

        wrapper.Initialize(
            serializer: default,
            type: default,
            timeout: replyOpts?.Timeout,
            startUpTimeout: replyOpts?.StartUpTimeout,
            idleTimeout: replyOpts?.IdleTimeout,
            maxMsgCount: replyOpts?.MaxMsgs ?? default,
            cancellationToken: cancellationTimer.Token);

        var replyTo = inboxSubscriber.Register(wrapper);
        try
        {
            await nats.PubAsync(subject, replyTo, payload, requestOpts?.Headers, cancellationToken)
                .ConfigureAwait(false);

            var msgCarrier = await wrapper.MsgRetrieveAsync().ConfigureAwait(false);

            nats.ObjectPool.Return(wrapper);
            cancellationTimer.TryReturn();

            if (msgCarrier.Termination != NatsMsgCarrierTermination.None)
            {
                yield break;
            }

            // Sentinel
            if (msgCarrier.BufferData?.Length == 0)
            {
                yield break;
            }

            yield return msgCarrier.ToMsg();
        }
        finally
        {
            inboxSubscriber.Unregister(replyTo);
        }
    }

    public static IAsyncEnumerable<NatsMsg> RequestManyAsync(
        this NatsConnection nats,
        NatsMsg msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestManyAsync(nats, msg.Subject, new ReadOnlySequence<byte>(msg.Data), default, replyOpts, cancellationToken);

    public static async IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        this NatsConnection nats,
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var serializer = replyOpts?.Serializer ?? nats.Options.Serializer;
        var inboxSubscriber = nats.InboxSubscriber;
        await inboxSubscriber.EnsureStartedAsync().ConfigureAwait(false);

        var cancellationTimer = nats.GetCancellationTimer(cancellationToken);

        if (!nats.ObjectPool.TryRent<MsgWrapper>(out var wrapper))
        {
            wrapper = new MsgWrapper();
        }

        wrapper.Initialize(
            serializer: serializer,
            type: typeof(TReply),
            timeout: replyOpts?.Timeout,
            startUpTimeout: replyOpts?.StartUpTimeout,
            idleTimeout: replyOpts?.IdleTimeout,
            maxMsgCount: replyOpts?.MaxMsgs ?? default,
            cancellationToken: cancellationTimer.Token);

        var replyTo = inboxSubscriber.Register(wrapper);
        try
        {
            await nats.PubModelAsync(subject, data, requestOpts?.Serializer ?? nats.Options.Serializer, replyTo, requestOpts?.Headers, cancellationToken)
                .ConfigureAwait(false);

            var msgCarrier = await wrapper.MsgRetrieveAsync().ConfigureAwait(false);

            nats.ObjectPool.Return(wrapper);
            cancellationTimer.TryReturn();

            if (msgCarrier.Termination != NatsMsgCarrierTermination.None)
            {
                yield break;
            }

            // Sentinel
            if (msgCarrier.ModelData == default)
            {
                yield break;
            }

            yield return msgCarrier.ToMsg<TReply?>();
        }
        finally
        {
            inboxSubscriber.Unregister(replyTo);
        }
    }

    public static IAsyncEnumerable<NatsMsg<TReply?>> RequestManyAsync<TRequest, TReply>(
        this NatsConnection nats,
        NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestManyAsync<TRequest, TReply>(nats, msg.Subject, msg.Data, default, replyOpts, cancellationToken);
}
