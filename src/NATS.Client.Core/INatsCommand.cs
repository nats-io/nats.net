using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

// ***Async or Post***Async(fire-and-forget)
public interface INatsCommand
{
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default);

    // void PostPing(CancellationToken cancellationToken = default);

    // publish
    ValueTask PublishAsync(string subject, ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(NatsMsg msg, CancellationToken cancellationToken = default);

    ValueTask PublishAsync<T>(string subject, T data, in NatsPubOpts? opts = default, CancellationToken cancellationToken = default);

    ValueTask PublishAsync<T>(NatsMsg<T> msg, CancellationToken cancellationToken = default);

    // void PostPublish(string subject, ReadOnlySequence<byte> data = default, in NatsPubOpts? opts = default);
    //
    // void PostPublish(NatsMsg msg);
    //
    // void PostPublish<T>(string subject, T data, in NatsPubOpts? opts = default);
    //
    // void PostPublish<T>(NatsMsg<T> msg);

    // subscribe
    ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    ValueTask<NatsSub<T>> SubscribeAsync<T>(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default);

    /****************************************************************************************************/

    // ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);
    //
    // ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(NatsKey key, TRequest request, CancellationToken cancellationToken = default);
    //
    // ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request, CancellationToken cancellationToken = default);

    // ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeAsync(string key, Action handler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);
    // ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default);
    //
    // ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default);

    /**************************************************************************/
    IObservable<T> AsObservable<T>(in NatsKey key);

    IObservable<T> AsObservable<T>(string key);

    // void PostDirectWrite(byte[] protocol);
    //
    // void PostDirectWrite(DirectWriteCommand command);
    //
    // void PostDirectWrite(string protocol, int repeatCount = 1);
}
