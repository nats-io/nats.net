using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal sealed class RequestAsyncCommand<TRequest, TResponse> : AsyncCommandBase<RequestAsyncCommand<TRequest, TResponse>, TResponse>
{
    private NatsKey _key;
    private TRequest? _request;
    private ReadOnlyMemory<byte> _inboxPrefix;
    private int _id;
    private INatsSerializer? _serializer;
    private CancellationTokenRegistration _cancellationTokenRegistration;
    private RequestResponseManager? _box;
    private bool _succeed;

    private RequestAsyncCommand()
    {
    }

    public static RequestAsyncCommand<TRequest, TResponse> Create(ObjectPool pool, in NatsKey key, ReadOnlyMemory<byte> inboxPrefix, int id, TRequest request, INatsSerializer serializer, CancellationToken cancellationToken, RequestResponseManager box)
    {
        if (!TryRent(pool, out var result))
        {
            result = new RequestAsyncCommand<TRequest, TResponse>();
        }

        result._key = key;
        result._inboxPrefix = inboxPrefix;
        result._id = id;
        result._request = request;
        result._serializer = serializer;
        result._succeed = false;
        result._box = box;

        if (cancellationToken.CanBeCanceled)
        {
            result._cancellationTokenRegistration = cancellationToken.Register(
                static cmd =>
                {
                    if (cmd is RequestAsyncCommand<TRequest, TResponse?> x)
                    {
                        lock (x)
                        {
                            // if succeed(after await), possibillity of already returned to pool so don't call SetException
                            if (!x._succeed)
                            {
                                if (x._box?.Remove(x._id) ?? false)
                                {
                                    x.SetException(new TimeoutException("Request timed out."));
                                }
                            }
                        }
                    }
                },
                result);
        }

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(_key, _inboxPrefix, _id, _request, _serializer!);
    }

    protected override void Reset()
    {
        lock (this)
        {
            try
            {
                _cancellationTokenRegistration.Dispose(); // stop cancellation timer.
            }
            catch
            {
            }

            _cancellationTokenRegistration = default;
            _key = default;
            _request = default;
            _inboxPrefix = null;
            _serializer = null;
            _box = null;
            _succeed = true;
            _id = 0;
        }
    }
}
