using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public readonly struct NatsReplyHandle : IAsyncDisposable
{
    private readonly NatsSubBase _sub;
    private readonly Task _reader;

    internal NatsReplyHandle(NatsSubBase sub, Task reader)
    {
        _sub = sub;
        _reader = reader;
    }

    public async ValueTask DisposeAsync()
    {
        await _sub.DisposeAsync().ConfigureAwait(false);
        await _reader.ConfigureAwait(false);
    }
}

public static class NatReplyUtils
{
    /// <summary>
    /// Create a responder using the NATS Request-Reply pattern, with a single response.
    /// </summary>
    /// <param name="nats">NATS connection</param>
    /// <param name="subject">Subject to subscribed to</param>
    /// <param name="reply">Callback to prepare replies to incoming requests. Exceptions will be handled and a default response will be sent. You should implement your own exception handling.</param>
    /// <typeparam name="TRequest">Incoming request type</typeparam>
    /// <typeparam name="TResponse">Reply or response type to be sent to requesters</typeparam>
    /// <returns>A disposable handler to keep track of subscription. Dispose to unsubscribe and wait for reply callback to exit.</returns>
    public static async Task<NatsReplyHandle> ReplyAsync<TRequest, TResponse>(this INatsConnection nats, string subject, Func<TRequest?, TResponse> reply)
    {
        var sub = await nats.SubscribeAsync<TRequest>(subject).ConfigureAwait(false);
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in sub.Msgs.ReadAllAsync())
            {
                try
                {
                    var response = reply(msg.Data);
                    await msg.ReplyAsync(response).ConfigureAwait(false);
                }
                catch
                {
                    await msg.ReplyAsync(default(TResponse)).ConfigureAwait(false);
                }
            }
        });
        return new NatsReplyHandle(sub, reader);
    }

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
