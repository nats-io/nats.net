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
        return new FireAndForgetDisposable(_connection.SubscribeAsync<T>(_key, observer.OnNext), observer.OnError);
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
}
