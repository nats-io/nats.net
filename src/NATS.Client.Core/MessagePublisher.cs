using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

// TODO: Clean up message publisher.
internal delegate Task PublishMessage(string subject, string? replyTo, NatsOptions options, ReadOnlySequence<byte> buffer, object?[] callbacks);

internal static class MessagePublisher
{
    // To avoid boxing, cache generic type and invoke it.
    private static readonly Func<Type, PublishMessage> CreatePublisherValue = CreatePublisher;
    private static readonly ConcurrentDictionary<Type, PublishMessage> PublisherCache = new();

    public static Task PublishAsync(string subject, string? replyTo, Type type, NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        return PublisherCache.GetOrAdd(type, CreatePublisherValue).Invoke(subject, replyTo, options, buffer, callbacks);
    }

    private static PublishMessage CreatePublisher(Type type)
    {
        if (type == typeof(byte[]))
        {
            return new ByteArrayMessagePublisher().Publish;
        }
        else if (type == typeof(ReadOnlyMemory<byte>))
        {
            return new ReadOnlyMemoryMessagePublisher().PublishAsync;
        }

        var publisher = typeof(MessagePublisher<>).MakeGenericType(type)!;
        var instance = Activator.CreateInstance(publisher)!;
        return (PublishMessage)Delegate.CreateDelegate(typeof(PublishMessage), instance, "Publish", false);
    }
}

internal sealed class MessagePublisher<T>
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        T? value;
        try
        {
            value = options!.Serializer.Deserialize<T>(buffer);
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(T).Name);
            }
            catch
            {
            }

            return;
        }

        try
        {
            if (!options.UseThreadPoolCallback)
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            ((Action<T?>)callback).Invoke(value);
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during publish callback.");
                        }
                    }
                }
            }
            else
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        var item = ThreadPoolWorkItem<T>.Create((Action<T?>)callback, value, options!.LoggerFactory);
                        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during publish callback.");
            }
            catch
            {
            }
        }
    }
}

internal sealed class ByteArrayMessagePublisher
{
#pragma warning disable CA1822
#pragma warning disable VSTHRD200
#pragma warning disable CS1998
    public async Task Publish(string subject, string? replyTo, NatsOptions? options, ReadOnlySequence<byte> buffer, object?[] callbacks)
#pragma warning restore CS1998
#pragma warning restore VSTHRD200
#pragma warning restore CA1822
    {
        byte[] value;
        try
        {
            if (buffer.IsEmpty)
            {
                value = Array.Empty<byte>();
            }
            else
            {
                value = buffer.ToArray();
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
            }
            catch
            {
            }

            return;
        }

        try
        {
            if (options is { UseThreadPoolCallback: false })
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            ((Action<byte[]?>)callback).Invoke(value);
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<ByteArrayMessagePublisher>().LogError(ex, "Error occured during publish callback.");
                        }
                    }
                }
            }
            else
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        var item = ThreadPoolWorkItem<byte[]>.Create((Action<byte[]?>)callback, value, options!.LoggerFactory);
                        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ByteArrayMessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch
            {
            }
        }
    }
}

internal sealed class ReadOnlyMemoryMessagePublisher
{
    public async Task PublishAsync(string subject, string? replyTo, NatsOptions? options, ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        ReadOnlyMemory<byte> value;
        try
        {
            if (buffer.IsEmpty)
            {
                value = Array.Empty<byte>();
            }
            else
            {
                value = buffer.ToArray();
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
            }
            catch
            {
            }

            return;
        }

        try
        {
            if (options is { UseThreadPoolCallback: false })
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            if (callback is NatsSubBase natsSub)
                            {
                                await natsSub.ReceiveAsync(subject, replyTo, buffer).ConfigureAwait(false);
                            }
                            else if (callback is Action<ReadOnlyMemory<byte>> action)
                            {
                                action.Invoke(value);
                            }
                            else
                            {
                                throw new NatsException($"Unexpected internal handler type: {callback.GetType().Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Error occured during publish callback.");
                        }
                    }
                }
            }
            else
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        var item = ThreadPoolWorkItem<ReadOnlyMemory<byte>>.Create((Action<ReadOnlyMemory<byte>>)callback, value, options!.LoggerFactory);
                        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch
            {
            }
        }
    }
}
