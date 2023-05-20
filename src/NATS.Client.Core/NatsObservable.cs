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
        var disp2 = new FireAndForgetDisposable(_connection.SubscribeAsync<T>(_key, observer.OnNext, disp.Token), observer.OnError);
        return new Tuple2Disposable(disp, disp2);
    }

    private sealed class FireAndForgetDisposable : IDisposable
    {
        private bool _disposed;
        private IDisposable? _taskDisposable;
        private Action<Exception> _onError;
        private object _gate = new object();

        public FireAndForgetDisposable(ValueTask<IDisposable> task, Action<Exception> onError)
        {
            _disposed = false;
            _onError = onError;
            FireAndForget(task);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_taskDisposable != null)
                {
                    _taskDisposable.Dispose();
                }
            }
        }

        private async void FireAndForget(ValueTask<IDisposable> task)
        {
            try
            {
                _taskDisposable = await task.ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        _taskDisposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _disposed = true;
                }

                _onError(ex);
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
