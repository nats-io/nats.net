using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class RequestAsyncCommand<TRequest, TResponse> : AsyncCommandBase<RequestAsyncCommand<TRequest, TResponse>, TResponse>
{
    NatsKey key;
    TRequest? request;
    ReadOnlyMemory<byte> inboxPrefix;
    int id;
    INatsSerializer? serializer;
    CancellationTokenRegistration cancellationTokenRegistration;
    RequestResponseManager? box;
    bool succeed;

    RequestAsyncCommand()
    {
    }

    public static RequestAsyncCommand<TRequest, TResponse> Create(ObjectPool pool, in NatsKey key, ReadOnlyMemory<byte> inboxPrefix, int id, TRequest request, INatsSerializer serializer, CancellationToken cancellationToken, RequestResponseManager box)
    {
        if (!TryRent(pool, out var result))
        {
            result = new RequestAsyncCommand<TRequest, TResponse>();
        }

        result.key = key;
        result.inboxPrefix = inboxPrefix;
        result.id = id;
        result.request = request;
        result.serializer = serializer;
        result.succeed = false;
        result.box = box;

        if (cancellationToken.CanBeCanceled)
        {
            result.cancellationTokenRegistration = cancellationToken.Register(static cmd =>
            {
                if (cmd is RequestAsyncCommand<TRequest, TResponse?> x)
                {
                    lock (x)
                    {
                        // if succeed(after await), possibillity of already returned to pool so don't call SetException
                        if (!x.succeed)
                        {
                            if (x.box?.Remove(x.id) ?? false)
                            {
                                x.SetException(new TimeoutException("Request timed out."));
                            }
                        }
                    }
                }
            }, result);
        }

        return result;
    }

    protected override void Reset()
    {
        lock (this)
        {
            try
            {
                cancellationTokenRegistration.Dispose(); // stop cancellation timer.
            }
            catch { }
            cancellationTokenRegistration = default;
            key = default;
            request = default;
            inboxPrefix = null;
            serializer = null;
            box = null;
            succeed = true;
            id = 0;
        }
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(key, inboxPrefix, id, request, serializer!);
    }
}
