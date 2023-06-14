namespace NATS.Client.Core;

internal sealed class NatsObservable<T> : IObservable<T>
{
    private readonly NatsConnection _connection;
    private readonly string _subject;

    public NatsObservable(NatsConnection connection, string subject)
    {
        _subject = subject;
        _connection = connection;
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        var disp = new CancellationTokenDisposable();
        var disp2 = new FireAndForgetDisposable(_connection.SubscribeAsync<T>(_subject, cancellationToken: disp.Token), observer);
        return new Tuple2Disposable(disp, disp2);
    }

    private sealed class FireAndForgetDisposable : IDisposable
    {
        private readonly IObserver<T> _observer;
        private bool _disposed;
        private IDisposable? _taskDisposable;
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
                    _taskDisposable.Dispose();
                }
            }
        }

        private async void FireAndForget(ValueTask<NatsSub<T>> natsSub, IObserver<T> observer)
        {
            try
            {
                var sub = await natsSub.ConfigureAwait(false);

                // TODO: Consider removing observable support from the API
                // * Channels and observables don't go together very well and creating a generic solution is
                //   problematic. An avid RX developer should be able to hook the channel up easily for their
                //   scenario.
                // * Below workaround isn't very well thought out and will probably create bugs except maybe
                //   in simple scenarios.
                _ = Task.Run(async () =>
                {
                    await foreach (var msg in sub.Msgs.ReadAllAsync())
                    {
                        observer.OnNext(msg.Data);
                    }
                });

                _taskDisposable = sub;

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
