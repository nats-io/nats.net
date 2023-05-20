using NATS.Client.Core.Commands;

namespace NATS.Client.Core;

// ***Async or Post***Async(fire-and-forget)
public interface INatsCommand
{
    IObservable<T> AsObservable<T>(in NatsKey key);

    IObservable<T> AsObservable<T>(string key);

    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default);

    void PostDirectWrite(byte[] protocol);

    void PostDirectWrite(DirectWriteCommand command);

    void PostDirectWrite(string protocol, int repeatCount = 1);

    void PostPing(CancellationToken cancellationToken = default);

    void PostPublish(in NatsKey key);

    void PostPublish(in NatsKey key, byte[] value);

    void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value);

    void PostPublish(string key);

    void PostPublish(string key, byte[] value);

    void PostPublish(string key, ReadOnlyMemory<byte> value);

    void PostPublish<T>(in NatsKey key, T value);

    void PostPublish<T>(string key, T value);

    void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values);

    void PostPublishBatch<T>(IEnumerable<(string, T?)> values);

    ValueTask PublishAsync(in NatsKey key, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(in NatsKey key, byte[] value, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(string key, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(string key, byte[] value, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default);

    ValueTask PublishAsync<T>(in NatsKey key, T value, CancellationToken cancellationToken = default);

    ValueTask PublishAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values, CancellationToken cancellationToken = default);

    ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);

    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(NatsKey key, TRequest request, CancellationToken cancellationToken = default);

    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeAsync(string key, Action handler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default);

    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default);
}
