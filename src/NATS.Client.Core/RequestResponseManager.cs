using System.Buffers;
using System.Text;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

internal sealed class RequestResponseManager : IDisposable
{
    private readonly NatsConnection _connection;
    private readonly ObjectPool _pool;
    private readonly object _gate = new object();
    private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    private int _requestId = 0; // unique id per connection
    private bool _isDisposed;

    // ID: Handler
    private Dictionary<int, (Type responseType, object handler)> _responseBoxes = new();
    private NatsSub? _globalSubscription;

    public RequestResponseManager(NatsConnection connection, ObjectPool pool)
    {
        _connection = connection;
        _pool = pool;
    }

    public ValueTask<TResponse?> AddAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request, CancellationToken cancellationToken)
    {
        if (_globalSubscription == null)
        {
            return AddWithGlobalSubscribeAsync<TRequest, TResponse>(key, inBoxPrefix, request, cancellationToken);
        }

        return AddAsyncCoreAsync<TRequest, TResponse>(key, inBoxPrefix, request, cancellationToken);
    }

    public void PublishToResponseHandler(int id, in ReadOnlySequence<byte> buffer)
    {
        (Type responseType, object handler) box;
        lock (_gate)
        {
            if (!_responseBoxes.Remove(id, out box))
            {
                return;
            }
        }

        ResponsePublisher.PublishResponse(box.responseType, _connection.Options, buffer, box.handler);
    }

    public bool Remove(int id)
    {
        lock (_gate)
        {
            return _responseBoxes.Remove(id, out _);
        }
    }

    // when socket disconnected, can not receive new one so set cancel all waiting promise.
    public void Reset()
    {
        lock (_gate)
        {
            foreach (var item in _responseBoxes)
            {
                if (item.Value.handler is IPromise p)
                {
                    p.SetCanceled();
                }
            }

            _responseBoxes.Clear();

#pragma warning disable VSTHRD110
            _globalSubscription?.DisposeAsync();
#pragma warning restore VSTHRD110
            _globalSubscription = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cancellationTokenSource.Cancel();

        Reset();
    }

    private async ValueTask<TResponse?> AddWithGlobalSubscribeAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request, CancellationToken cancellationToken)
    {
        await _asyncLock.WaitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        try
        {
            if (_globalSubscription == null)
            {
                var globalSubscribeKey = $"{Encoding.ASCII.GetString(inBoxPrefix.Span)}*";
                _globalSubscription = await _connection.SubscribeAsync(globalSubscribeKey).ConfigureAwait(false);
            }
        }
        finally
        {
            _asyncLock.Release();
        }

        return await AddAsyncCoreAsync<TRequest, TResponse>(key, inBoxPrefix, request, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<TResponse?> AddAsyncCoreAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var command = RequestAsyncCommand<TRequest, TResponse?>.Create(_pool, key, inBoxPrefix, id, request, _connection.Options.Serializer, cancellationToken, this);

        lock (_gate)
        {
            if (_isDisposed)
                throw new NatsException("Connection is closed.");
            if (_globalSubscription == null)
                throw new NatsException("Connection is disconnected.");
            _responseBoxes.Add(id, (typeof(TResponse), command));
        }

        // MEMO: await has some performance loss, we should avoid await EnqueueAndAwait
        return await _connection.EnqueueAndAwaitCommandAsync(command).ConfigureAwait(false);
    }
}
