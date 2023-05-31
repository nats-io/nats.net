namespace NATS.Client.Core.Commands;

internal interface IPromise
{
    void SetResult();

    void SetCanceled();

    void SetException(Exception exception);
}

internal interface IPromise<T> : IPromise
{
    void SetResult(T result);
}
