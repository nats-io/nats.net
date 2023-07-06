using System.Buffers;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsRequestAsyncExtensions
{
    // RequestAsync methods
    // Same as PublishAsync with the following changes
    // - Response is 0 (null) or 1 NatsMsg
    // - PubOpts is called requestOpts
    // - add SubOpts replyOpts
    //   - default replyOpts.MaxMsgs to 1
    //   - if replyOpts.Timeout == null then set to NatsOptions.RequestTimeout
    public static async ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        this NatsConnection nats,
        string subject,
        TRequest? data,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
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
            await nats.PubModelAsync(subject, data, requestOpts?.Serializer ?? nats.Options.Serializer, replyTo, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var msgCarrier = await wrapper.MsgRetrieveAsync().ConfigureAwait(false);

            nats.ObjectPool.Return(wrapper);
            cancellationTimer.TryReturn();

            if (msgCarrier.Termination != NatsMsgCarrierTermination.None)
            {
                return null;
            }

            return msgCarrier.ToMsg<TReply?>();
        }
        finally
        {
            inboxSubscriber.Unregister(replyTo);
        }
    }

    public static ValueTask<NatsMsg<TReply?>?> RequestAsync<TRequest, TReply>(
        this NatsConnection nats,
        in NatsMsg<TRequest> msg,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestAsync<TRequest, TReply>(nats, msg.Subject, data: msg.Data, replyOpts: replyOpts, cancellationToken: cancellationToken);

    public static async ValueTask<NatsMsg?> RequestAsync(
        this NatsConnection nats,
        string subject,
        ReadOnlySequence<byte> payload = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
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
            await nats.PubAsync(subject, replyTo, payload, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var msgCarrier = await wrapper.MsgRetrieveAsync().ConfigureAwait(false);

            nats.ObjectPool.Return(wrapper);
            cancellationTimer.TryReturn();

            if (msgCarrier.Termination != NatsMsgCarrierTermination.None)
            {
                return null;
            }

            return msgCarrier.ToMsg();
        }
        finally
        {
            inboxSubscriber.Unregister(replyTo);
        }
    }

    public static ValueTask<NatsMsg?> RequestAsync(
        this NatsConnection nats,
        in NatsMsg msg,
        in NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        RequestAsync(nats, msg.Subject, new ReadOnlySequence<byte>(msg.Data), default, replyOpts, cancellationToken);
}
