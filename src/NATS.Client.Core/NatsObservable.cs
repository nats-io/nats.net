namespace NATS.Client.Core;

internal sealed class NatsObservable<T> : IObservable<T>
{
    private readonly NatsConnection _connection;
    private readonly NatsKey _key;

    public NatsObservable(NatsConnection connection, in NatsKey key)
    {
        _key = key;
        _connection = connection;
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        var disp = new CancellationTokenDisposable();
        var disp2 = new FireAndForgetDisposable(_connection.SubscribeAsync<T>(_key.Key, cancellationToken: disp.Token), observer);
        return new Tuple2Disposable(disp, disp2);
    }

    private sealed class FireAndForgetDisposable : IDisposable
    {
        private readonly IObserver<T> _observer;
        private bool _disposed;
        private IAsyncDisposable? _taskDisposable;
        private object _gate = new object();

        public FireAndForgetDisposable(ValueTask<NatsSub<T>> natsSub, IObserver<T> observer)
        {
            _observer = observer;
            _disposed = false;
            FireAndForget(natsSub, observer);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_taskDisposable != null)
                {
#pragma warning disable VSTHRD110
                    _taskDisposable.DisposeAsync();
#pragma warning restore VSTHRD110
                }
            }
        }

        private async void FireAndForget(ValueTask<NatsSub<T>> natsSub, IObserver<T> observer)
        {
            try
            {
                var sub = await natsSub.ConfigureAwait(false);
                sub.Register(msg => observer.OnNext(msg.Data));

                _taskDisposable = sub;

                var task = ValueTask.CompletedTask;

                lock (_gate)
                {
                    if (_disposed)
                    {
                        task = _taskDisposable.DisposeAsync();
                    }
                }

                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _disposed = true;
                }

                _observer.OnError(ex);
            }
        }
    }

    private class CancellationTokenDisposable : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;

        public CancellationTokenDisposable()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public CancellationToken Token => _cancellationTokenSource.Token;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
        }
    }

    private class Tuple2Disposable : IDisposable
    {
        private readonly IDisposable _disposable1;
        private readonly IDisposable _disposable2;

        public Tuple2Disposable(IDisposable disposable1, IDisposable disposable2)
        {
            _disposable1 = disposable1;
            _disposable2 = disposable2;
        }

        public void Dispose()
        {
            _disposable1.Dispose();
            _disposable2.Dispose();
        }
    }
}
