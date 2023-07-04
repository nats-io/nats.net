using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsRequestExtensions
{
    /// <summary>
    /// Request data using the NATSRequest-Reply pattern with a single response.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="subject">Subject the responder subscribed to</param>
    /// <param name="data">Request data</param>
    /// <param name="cancellationToken">Cancellation token for cancelling the request</param>
    /// <param name="timeout">Allowed time span before the request is cancelled</param>
    /// <typeparam name="TRequest">Request type being sent</typeparam>
    /// <typeparam name="TReply">Reply or response type received</typeparam>
    /// <returns>Data sent by the responder</returns>
    public static async ValueTask<TReply?> RequestAsync<TRequest, TReply>(this NatsConnection nats, string subject, TRequest data, CancellationToken cancellationToken = default, TimeSpan timeout = default)
    {
        var serializer = nats.Options.Serializer;
        var inboxSubscriber = nats.InboxSubscriber;
        await inboxSubscriber.EnsureStartedAsync().ConfigureAwait(false);

        if (!nats.ObjectPool.TryRent<MsgWrapper>(out var wrapper))
        {
            wrapper = new MsgWrapper();
        }

        var cancellationTimer = nats.GetCancellationTimer(cancellationToken, timeout);
        wrapper.SetSerializer<TReply>(serializer, cancellationTimer.Token);

        var replyTo = inboxSubscriber.Register(wrapper);
        try
        {
            await nats.PubModelAsync<TRequest>(subject, data, serializer, replyTo, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var dataReply = await wrapper.MsgRetrieveAsync().ConfigureAwait(false);

            nats.ObjectPool.Return(wrapper);
            cancellationTimer.TryReturn();

            return (TReply?)dataReply;
        }
        finally
        {
            inboxSubscriber.Unregister(replyTo);
        }
    }
}
