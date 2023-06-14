using System.Text;

namespace NATS.Client.Core;

public static class NatReplyUtils
{
    public static async Task<NatsReplyHandle> ReplyAsync<TRequest, TResponse>(this INatsCommand nats, string subject, Func<TRequest, TResponse> reply)
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

    public static async Task<TResponse> RequestAsync<TRequest, TResponse>(this INatsCommand nats, string subject, TRequest request)
    {
        var bs = ((NatsConnection)nats).InboxPrefix.ToArray(); // TODO: Fix inbox prefix
        var replyTo = $"{Encoding.ASCII.GetString(bs)}{Guid.NewGuid():N}";

        // TODO: Optimize by using connection wide inbox subscriber
        var sub = await nats.SubscribeAsync<TResponse>(replyTo).ConfigureAwait(false);

        await nats.PublishAsync(subject, request, new NatsPubOpts { ReplyTo = replyTo }).ConfigureAwait(false);

        return await Task.Run(async () =>
        {
            try
            {
                // TODO: Implement configurable request timeout
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
                {
                    return msg.Data;
                }

                throw new NatsException("Request-reply subscriber closed unexpectedly");
            }
            catch (OperationCanceledException e)
            {
                throw new TimeoutException("Request-reply timed-out", e);
            }
            finally
            {
                await sub.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }
}

public class NatsReplyHandle : IAsyncDisposable
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
