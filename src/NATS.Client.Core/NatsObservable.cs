namespace NATS.Client.Core;

internal sealed class NatsObservable<T> : IObservable<T>
{
    readonly NatsConnection connection;
    readonly NatsKey key;

    public NatsObservable(NatsConnection connection, in NatsKey key)
    {
        this.key = key;
        this.connection = connection;
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        return new FireAndForgetDisposable(connection.SubscribeAsync<T>(key, observer.OnNext), observer.OnError);
    }

    sealed class FireAndForgetDisposable : IDisposable
    {
        bool disposed;
        IDisposable? taskDisposable;
        Action<Exception> onError;
        object gate = new object();

        public FireAndForgetDisposable(ValueTask<IDisposable> task, Action<Exception> onError)
        {
            this.disposed = false;
            this.onError = onError;
            FireAndForget(task);
        }

        async void FireAndForget(ValueTask<IDisposable> task)
        {
            try
            {
                taskDisposable = await task.ConfigureAwait(false);
                lock (gate)
                {
                    if (disposed)
                    {
                        taskDisposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    disposed = true;
                }
                onError(ex);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                if (taskDisposable != null)
                {
                    taskDisposable.Dispose();
                }
            }
        }
    }
}
